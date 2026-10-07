import type { BotGroup, Catalog, CatalogQuest, ItemRef, LootSource } from '../api/catalog'
import type { Holding, ProfileProgress, QuestStatus } from '../api/progress'
import { orderTraders } from '../wiki/derive'
import { holdingOf, itemKey, questProgress, remaining, sortForTab } from './derive'

// 카파 트래커(dev-docs/12-kappa-tracker/kappa-tracker.spec.md §2). 고정 목록 없이 실행 중 서버의 Collector 조건을 읽는다 —
// 바닐라(퀘스트 136 → 251)와 sptQuestLive(상인 LL4 + 퀘스트 4 → 29)를 같은 코드로 처리한다. derive.ts 와 같이 순수 함수.

export const COLLECTOR_ID = '5c51aac186f77432ea65c552'

export interface KappaGraph {
  goal: CatalogQuest
  /** 카파 퀘스트 = Collector 의 퀘스트 조건 + 그 선행 전부(완료를 요구하는 조건만 따라감). Collector 제외 */
  ids: ReadonlySet<string>
  /** 카파 퀘스트 → 그것이 요구하는 카파 퀘스트(완료 조건만) */
  ups: ReadonlyMap<string, string[]>
  /** 카파 퀘스트 → 그것을 요구하는 카파 퀘스트 */
  downs: ReadonlyMap<string, string[]>
  /** 가리키는 조건이 전부 실패도 인정하는 퀘스트 — 택일 분기에서 실패해도 완료로 센다(§2.3) */
  failOk: ReadonlySet<string>
}

const needsSuccess = (needStatuses: string[]) => needStatuses.includes('Success')

export function kappaGraph(catalog: Catalog): KappaGraph | null {
  const goal = catalog.quests[COLLECTOR_ID]
  if (!goal) return null
  const ids = new Set<string>()
  const stack = goal.requirements.flatMap((r) => (r.kind === 'quest' && needsSuccess(r.needStatuses) ? [r.questId] : []))
  while (stack.length) {
    const id = stack.pop()!
    const q = catalog.quests[id]
    if (ids.has(id) || !q || id === COLLECTOR_ID) continue
    ids.add(id)
    for (const r of q.requirements) if (r.kind === 'quest' && needsSuccess(r.needStatuses)) stack.push(r.questId)
  }

  const ups = new Map<string, string[]>()
  const downs = new Map<string, string[]>()
  const refs = new Map<string, string[][]>()
  for (const id of ids) { ups.set(id, []); downs.set(id, []) }
  for (const from of [COLLECTOR_ID, ...ids]) {
    for (const r of catalog.quests[from].requirements) {
      if (r.kind !== 'quest' || !ids.has(r.questId)) continue
      refs.set(r.questId, [...(refs.get(r.questId) ?? []), r.needStatuses])
      if (from !== COLLECTOR_ID && needsSuccess(r.needStatuses) && !ups.get(from)!.includes(r.questId)) {
        ups.get(from)!.push(r.questId)
        downs.get(r.questId)!.push(from)
      }
    }
  }
  const failOk = new Set([...refs].filter(([, all]) => all.every((s) => s.includes('Fail'))).map(([id]) => id))
  return { goal, ids, ups, downs, failOk }
}

const FAILED = new Set<QuestStatus>(['Fail', 'MarkedAsFailed', 'Expired'])

/** 완료로 세는 카파 퀘스트 — 성공, 또는 실패해도 되는 택일 분기에서 실패 */
export function kappaDone(g: KappaGraph, progress: ProfileProgress): Set<string> {
  const done = new Set<string>()
  for (const id of g.ids) {
    const s = questProgress(progress, id).status
    if (s === 'Success' || (FAILED.has(s) && g.failOk.has(id))) done.add(id)
  }
  return done
}

// ---- 조건 ----

export interface TraderCond { traderId: string; kind: 'loyalty' | 'standing'; need: number; current: number | null; met: boolean }

export interface KappaConds {
  level: { need: number; current: number; met: boolean } | null
  traders: TraderCond[]
  quests: { done: number; total: number }
}

/** 상인·레벨 충족 여부는 Collector 의 잠김 사유로 안다 — 서버는 못 채운 조건만 현재 값과 함께 보낸다(§1.3) */
export function kappaConds(g: KappaGraph, progress: ProfileProgress, done: ReadonlySet<string>): KappaConds {
  const qp = questProgress(progress, g.goal.id)
  const locked = qp.status === 'Locked'
  let level: KappaConds['level'] = null
  const traders: TraderCond[] = []
  for (const r of g.goal.requirements) {
    if (r.kind === 'level' && r.value > 1 && (level?.need ?? 0) < r.value) {
      level = { need: r.value, current: progress.level, met: !locked || progress.level >= r.value }
    }
    if (r.kind === 'traderLoyalty' || r.kind === 'traderStanding') {
      const reason = locked ? qp.lockReasons.find((x) => x.kind === r.kind && x.traderId === r.traderId) : undefined
      const current = reason && (reason.kind === 'traderLoyalty' || reason.kind === 'traderStanding') ? reason.current : null
      traders.push({ traderId: r.traderId, kind: r.kind === 'traderLoyalty' ? 'loyalty' : 'standing', need: r.value, current, met: !reason })
    }
  }
  return { level, traders, quests: { done: done.size, total: g.ids.size } }
}

// ---- 제출 아이템 ----

/** given = 이미 제출함, ready = 제출할 만큼 보유(FIR 요구면 FIR), nonFir = 보유는 있으나 FIR 부족 */
export type ItemState = 'given' | 'ready' | 'nonFir' | 'none'

export interface KappaItem { conditionId: string; items: ItemRef[]; state: ItemState }

export function kappaItems(g: KappaGraph, progress: ProfileProgress, inventory: Record<string, Holding>): KappaItem[] {
  const qp = questProgress(progress, g.goal.id)
  const out: KappaItem[] = []
  for (const o of g.goal.objectives) {
    const item = o.prep?.item
    if (!item || item.action !== 'handover') continue
    const left = remaining(o, qp.objectives[o.conditionId])
    const h = holdingOf(itemKey(item), inventory)
    const usable = item.foundInRaid ? h.fir : h.count
    const state: ItemState = left === 0 ? 'given' : usable >= left ? 'ready' : h.count > 0 ? 'nonFir' : 'none'
    out.push({ conditionId: o.conditionId, items: item.items, state })
  }
  return out
}

export const itemDone = (i: KappaItem) => i.state === 'given' || i.state === 'ready'

const BOT_ORDER: readonly BotGroup[] = ['scav', 'pmc', 'boss', 'raider', 'cultist', 'other']

/** 제출 조건의 출처. 대체 tpl 이 여럿이면 합쳐서 컨테이너마다 큰 확률. 출처 정보가 없는 아이템·구버전 서버면 null */
export function itemSource(item: KappaItem, sources: Record<string, LootSource> | undefined): LootSource | null {
  const found = item.items.map((i) => sources?.[i.tpl]).filter((x): x is LootSource => !!x)
  if (found.length === 0) return null
  if (found.length === 1) return found[0]
  const best = new Map<string, LootSource['containers'][number]>()
  for (const c of found.flatMap((f) => f.containers)) if ((best.get(c.tpl)?.chance ?? -1) < c.chance) best.set(c.tpl, c)
  const bots = new Set(found.flatMap((f) => f.bots))
  return { containers: [...best.values()].sort((a, b) => b.chance - a.chance), bots: BOT_ORDER.filter((b) => bots.has(b)) }
}

/**
 * 게임 이름이 무엇인지 알기 어려운 컨테이너 → UI 문자열 키. "일반 자금보관소(Common fund stash)"는 삼림에 하나뿐인
 * 슈투르만 열쇠로 여는 은닉처(boss_container)라 그렇게 부른다.
 */
export const CONTAINER_LABELS: Readonly<Record<string, 'kappa.container.shturman'>> = {
  '5d07b91b86f7745a077a9432': 'kappa.container.shturman',
}

/** 확률 표기: 0.1% 미만은 "<0.1%", 그 밖은 소수 첫째 자리 */
export function formatChance(chance: number): string {
  const pct = chance * 100
  return pct < 0.1 ? '<0.1%' : `${pct.toFixed(1)}%`
}

// ---- 행 숫자: 뒤로 N개 · 연쇄 N단계 ----

export interface ChainStat { after: number; chain: number }

/** 미완료 카파 퀘스트마다 뒤로 닿는 미완료 카파 퀘스트 수와 최장 연쇄 길이(자신 제외) */
export function chainStats(g: KappaGraph, done: ReadonlySet<string>): Map<string, ChainStat> {
  const depth = new Map<string, number>()
  const longest = (id: string): number => {
    const memo = depth.get(id)
    if (memo !== undefined) return memo
    depth.set(id, 0) // 순환 방어
    let d = 0
    for (const n of g.downs.get(id) ?? []) if (!done.has(n)) d = Math.max(d, longest(n) + 1)
    depth.set(id, d)
    return d
  }
  const stats = new Map<string, ChainStat>()
  for (const id of g.ids) {
    if (done.has(id)) continue
    const seen = new Set<string>()
    const stack = [...(g.downs.get(id) ?? [])]
    while (stack.length) {
      const n = stack.pop()!
      if (seen.has(n)) continue
      seen.add(n)
      stack.push(...(g.downs.get(n) ?? []))
    }
    stats.set(id, { after: [...seen].filter((n) => !done.has(n)).length, chain: longest(id) })
  }
  return stats
}

// ---- 목록 ----

const NOW = new Set<QuestStatus>(['AvailableForFinish', 'Started', 'AvailableForStart', 'FailRestartable'])

export interface KappaLists { now: CatalogQuest[]; locked: CatalogQuest[]; done: CatalogQuest[] }

/** 지금 할 수 있는 것(연쇄 길이 ↓ → 뒤로 N ↓ → 이름) / 잠김(곧 열리는 순) / 완료(최신순). traderId 가 있으면 그 상인만 */
export function kappaLists(
  catalog: Catalog, g: KappaGraph, progress: ProfileProgress, done: ReadonlySet<string>, stats: ReadonlyMap<string, ChainStat>,
  traderId: string | null = null,
): KappaLists {
  const now: CatalogQuest[] = []
  const locked: CatalogQuest[] = []
  const finished: CatalogQuest[] = []
  for (const id of g.ids) {
    const q = catalog.quests[id]
    if (traderId && q.traderId !== traderId) continue
    if (done.has(id)) finished.push(q)
    else if (NOW.has(questProgress(progress, id).status)) now.push(q)
    else locked.push(q)
  }
  const s = (id: string) => stats.get(id) ?? { after: 0, chain: 0 }
  now.sort((a, b) => s(b.id).chain - s(a.id).chain || s(b.id).after - s(a.id).after || a.name.localeCompare(b.name))
  return { now, locked: sortForTab(locked, progress, 'locked'), done: sortForTab(finished, progress, 'done') }
}

/** 아직 아무 퀘스트도 열리지 않은 프로필 — 게임에 한 번도 접속하지 않으면 서버가 전부 Locked 로 준다 */
export function nothingOpen(progress: ProfileProgress): boolean {
  return Object.values(progress.quests).every((q) => q.status === 'Locked')
}

// ---- 끝내면 열림 ----

/** 아직 못 채운 퀘스트 선행 조건(서버가 보낸 잠김 사유 중) */
const openQuestReasons = (progress: ProfileProgress, id: string) =>
  questProgress(progress, id).lockReasons.flatMap((r) =>
    r.kind === 'quest' && !r.needStatuses.includes(r.currentStatus) ? [r] : [])

/** 퀘스트 → 그 퀘스트에 막혀 있는 잠긴 카파 퀘스트(이름순). "지금 할 수 있는 것" 줄 오른쪽의 "→ ○○ 외 N개" */
export function kappaUnlocks(catalog: Catalog, g: KappaGraph, progress: ProfileProgress, done: ReadonlySet<string>): Map<string, CatalogQuest[]> {
  const out = new Map<string, CatalogQuest[]>()
  for (const id of g.ids) {
    if (done.has(id) || questProgress(progress, id).status !== 'Locked') continue
    for (const r of openQuestReasons(progress, id)) out.set(r.questId, [...(out.get(r.questId) ?? []), catalog.quests[id]])
  }
  for (const list of out.values()) list.sort((a, b) => a.name.localeCompare(b.name))
  return out
}

// ---- 상인 표 ----

export interface TraderRow { traderId: string; done: number; total: number; conds: TraderCond[] }

export function traderRows(catalog: Catalog, g: KappaGraph, done: ReadonlySet<string>, conds: TraderCond[]): TraderRow[] {
  const rows = new Map<string, TraderRow>()
  const row = (traderId: string) => {
    let r = rows.get(traderId)
    if (!r) rows.set(traderId, (r = { traderId, done: 0, total: 0, conds: [] }))
    return r
  }
  for (const id of g.ids) {
    const r = row(catalog.quests[id].traderId)
    r.total++
    if (done.has(id)) r.done++
  }
  for (const c of conds) row(c.traderId).conds.push(c)
  const order = orderTraders(catalog.traders).map((t) => t.id)
  const rank = (id: string) => (order.includes(id) ? order.indexOf(id) : order.length)
  return [...rows.values()].sort((a, b) => rank(a.traderId) - rank(b.traderId))
}

// ---- 연쇄 팝업 ----

export interface ChainTiers {
  /** [0] = 바로 위(−1), [1] = −2 … 각 단계는 이름순 */
  ups: string[][]
  /** [0] = 바로 아래(+1) … */
  downs: string[][]
}

/** 퀘스트 하나의 카파 조상·자손을 최장 거리 단계로. 카파 밖의 퀘스트는 위쪽만 비고 아래쪽은 그대로 계산된다 */
export function chainTiers(catalog: Catalog, g: KappaGraph, questId: string): ChainTiers {
  const reach = (next: ReadonlyMap<string, string[]>, start: string[]) => {
    const seen = new Set<string>()
    const stack = [...start]
    while (stack.length) {
      const id = stack.pop()!
      if (seen.has(id)) continue
      seen.add(id)
      stack.push(...(next.get(id) ?? []))
    }
    return seen
  }
  const directUps = g.ids.has(questId) ? g.ups.get(questId)! : catalog.quests[questId]?.requirements
    .flatMap((r) => (r.kind === 'quest' && g.ids.has(r.questId) && needsSuccess(r.needStatuses) ? [r.questId] : [])) ?? []
  const directDowns = g.downs.get(questId) ?? []
  const tiers = (members: Set<string>, toward: ReadonlyMap<string, string[]>, direct: string[]) => {
    // dist(x) = x 에서 질문 퀘스트 쪽으로 가는 이웃 중 최장 + 1. 질문 퀘스트와 바로 닿으면 1
    const dist = new Map<string, number>()
    const of = (id: string): number => {
      const memo = dist.get(id)
      if (memo !== undefined) return memo
      dist.set(id, 1)
      let d = direct.includes(id) ? 1 : 0
      for (const n of toward.get(id) ?? []) if (members.has(n)) d = Math.max(d, of(n) + 1)
      dist.set(id, d)
      return d
    }
    const out: string[][] = []
    for (const id of members) (out[of(id) - 1] ??= []).push(id)
    return out.filter(Boolean).map((t) => t.sort((a, b) => catalog.quests[a].name.localeCompare(catalog.quests[b].name)))
  }
  return {
    ups: tiers(reach(g.ups, directUps), g.downs, directUps),
    downs: tiers(reach(g.downs, directDowns), g.ups, directDowns),
  }
}
