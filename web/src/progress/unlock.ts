import type { Catalog } from '../api/catalog'
import type { ProfileProgress, QuestStatus } from '../api/progress'
import { branchIndex, type BranchInfo } from '../wiki/derive'
import { isClosed, OTHER_CATEGORY, questProgress, searchKey } from './derive'

// 해금 경로(진행현황 2차, dev-docs/07-progress/unlock-planner.spec.md §1). 판매 해금·제작법 보상을 아이템별로 모아
// "지금 프로필에서 어떤 퀘스트를 어떤 순서로 깨야 하나"를 계산한다. derive.ts 와 같이 인자만으로 계산하는 순수 함수.

export interface UnlockSource {
  questId: string
  kind: 'sale' | 'craft'
  /** started = 퀘스트를 수락만 하면 해금 */
  phase: 'success' | 'started'
  traderId: string | null
  loyaltyLevel: number | null
  areaType: number | null
}

export interface PathStep { questId: string; mode: 'complete' | 'start' | 'fail'; depth: number; status: QuestStatus; failsOnComplete: string[] }
export type BlockKind = 'failed' | 'faction' | 'branch' | 'unknownLock'
export interface TraderReq { traderId: string; kind: 'loyalty' | 'standing'; value: number }

export interface SourcePlan {
  source: UnlockSource
  state: 'unlocked' | 'reachable' | 'blocked'
  blockReason: { kind: BlockKind; questId: string } | null
  /** 위상 순서(depth → 상인 → 이름), 목표가 마지막 */
  steps: PathStep[]
  remaining: number
  maxLevel: number | null
  traderReqs: TraderReq[]
}

const FAILED = new Set<QuestStatus>(['Fail', 'MarkedAsFailed', 'Expired'])
const UNLOCKED_ON_START = new Set<QuestStatus>(['Started', 'AvailableForFinish', 'Success'])
const NOW_ORDER: QuestStatus[] = ['AvailableForFinish', 'Started', 'AvailableForStart']

export function sourcePath(
  catalog: Catalog, progress: ProfileProgress, source: UnlockSource, branches: ReadonlyMap<string, BranchInfo> = new Map(),
): SourcePlan {
  const statusOf = (id: string) => questProgress(progress, id).status
  const target = statusOf(source.questId)
  const empty = { blockReason: null, steps: [], remaining: 0, maxLevel: null, traderReqs: [] }
  if (target === 'Success' || (source.phase === 'started' && UNLOCKED_ON_START.has(target))) return { source, state: 'unlocked', ...empty }

  const modes = new Map<string, PathStep['mode']>()
  const deps = new Map<string, string[]>()
  let blockReason: SourcePlan['blockReason'] = null
  const block = (kind: BlockKind, questId: string) => { blockReason ??= { kind, questId } }
  let maxLevel: number | null = null
  const traders = new Map<string, TraderReq>()

  const visit = (id: string, mode: PathStep['mode']) => {
    const seen = modes.get(id)
    if (seen) {
      if (seen === 'start' && mode === 'complete') modes.set(id, mode)
      return
    }
    modes.set(id, mode)
    deps.set(id, [])
    const q = catalog.quests[id]
    const qp = questProgress(progress, id)
    if (mode !== 'fail' && FAILED.has(qp.status)) block('failed', id)
    if (q?.factionOnly && q.factionOnly !== progress.side?.toLowerCase()) block('faction', id)
    if (qp.status === 'Locked' && qp.lockReasons.length === 0) block('unknownLock', id)
    if (mode === 'fail' || !q) return

    for (const r of q.requirements) {
      // 레벨 1 은 누구나 충족 — 남기면 "최대 레벨 1 ✓" 이 붙는다(목업은 "레벨 제한 없음")
      if (r.kind === 'level' && r.value > 1) maxLevel = Math.max(maxLevel ?? 0, r.value)
      if (r.kind === 'traderLoyalty' || r.kind === 'traderStanding') {
        const kind = r.kind === 'traderLoyalty' ? 'loyalty' : 'standing'
        const key = `${r.traderId}|${kind}`
        if ((traders.get(key)?.value ?? -Infinity) < r.value) traders.set(key, { traderId: r.traderId, kind, value: r.value })
      }
      if (r.kind !== 'quest') continue
      const cur = statusOf(r.questId)
      if (r.needStatuses.includes(cur)) continue
      // 이미 끝난 선행이 다른 결과를 요구 — 실패해 버렸으면 failed, 완료해 버렸으면 택일 분기(branch)
      if (isClosed(cur)) { block(FAILED.has(cur) ? 'failed' : 'branch', r.questId); continue }
      deps.get(id)!.push(r.questId)
      const need = r.needStatuses
      visit(r.questId, need.includes('Started') ? 'start' : need.includes('Success') || !need.includes('Fail') ? 'complete' : 'fail')
    }
  }
  visit(source.questId, source.phase === 'started' ? 'start' : 'complete')

  const depths = new Map<string, number>()
  const depthOf = (id: string): number => {
    const known = depths.get(id)
    if (known !== undefined) return known
    depths.set(id, 0) // 순환 방지
    const d = Math.max(-1, ...(deps.get(id) ?? []).map(depthOf)) + 1
    depths.set(id, d)
    return d
  }
  const traderName = (id: string) => catalog.traders[catalog.quests[id]?.traderId ?? '']?.name ?? catalog.quests[id]?.traderId ?? ''
  const nameOf = (id: string) => catalog.quests[id]?.name ?? id
  const steps: PathStep[] = [...modes].map(([questId, mode]) => ({
    questId, mode, depth: depthOf(questId), status: statusOf(questId), failsOnComplete: branches.get(questId)?.failsOnComplete ?? [],
  }))
  steps.sort((a, b) => Number(a.questId === source.questId) - Number(b.questId === source.questId)
    || a.depth - b.depth || traderName(a.questId).localeCompare(traderName(b.questId)) || nameOf(a.questId).localeCompare(nameOf(b.questId)))

  const traderReqs = [...traders.values()].sort((a, b) => a.traderId.localeCompare(b.traderId) || a.kind.localeCompare(b.kind))
  return { source, state: blockReason ? 'blocked' : 'reachable', blockReason, steps, remaining: steps.length, maxLevel, traderReqs }
}

/** 지금 손댈 수 있는 단계(완료 가능·진행 중·수락 가능) */
export function canDoNow(step: PathStep): boolean {
  return NOW_ORDER.includes(step.status)
}

/** 지금 할 것 — 완료 가능 → 진행 중 → 수락 가능 순. 없으면 null(첫 단계의 잠김 사유를 보여 준다). */
export function nextStep(plan: SourcePlan): PathStep | null {
  for (const s of NOW_ORDER) {
    const hit = plan.steps.find((p) => p.status === s)
    if (hit) return hit
  }
  return null
}

// ---- 아이템 한 줄 ----

export interface UnlockRow {
  tpl: string
  name: string
  category: string
  /** reachable(남은 수 오름차순) → blocked → unlocked */
  plans: SourcePlan[]
  state: 'unlocked' | 'remaining' | 'unreachable'
  /** 제작은 이미 해금, 판매는 아직 */
  craftUnlocked: boolean
  best: SourcePlan | null
}

/** tpl → 이름 + 입수처. 카탈로그 퀘스트 순서대로 success → started 보상. */
export function unlockSources(catalog: Catalog): Map<string, { name: string; sources: UnlockSource[] }> {
  const out = new Map<string, { name: string; sources: UnlockSource[] }>()
  for (const q of Object.values(catalog.quests)) {
    for (const phase of ['success', 'started'] as const) {
      for (const r of q.rewards[phase]) {
        if ((r.kind !== 'assortUnlock' && r.kind !== 'production') || !r.tpl) continue
        const entry = out.get(r.tpl) ?? { name: '', sources: [] }
        entry.name ||= r.name?.trim() ?? ''
        entry.sources.push(r.kind === 'assortUnlock'
          ? { questId: q.id, kind: 'sale', phase, traderId: r.traderId, loyaltyLevel: r.loyaltyLevel, areaType: null }
          : { questId: q.id, kind: 'craft', phase, traderId: null, loyaltyLevel: null, areaType: r.areaType })
        out.set(r.tpl, entry)
      }
    }
  }
  return out
}

const PLAN_ORDER = { reachable: 0, blocked: 1, unlocked: 2 }

export function unlockRows(catalog: Catalog, progress: ProfileProgress): UnlockRow[] {
  const branches = branchIndex(Object.values(catalog.quests))
  const rows: UnlockRow[] = []
  for (const [tpl, { name, sources }] of unlockSources(catalog)) {
    const plans = sources.map((s) => sourcePath(catalog, progress, s, branches))
      .sort((a, b) => PLAN_ORDER[a.state] - PLAN_ORDER[b.state] || a.remaining - b.remaining)
    const unlocked = (kind: UnlockSource['kind']) => plans.some((p) => p.source.kind === kind && p.state === 'unlocked')
    const hasSale = sources.some((s) => s.kind === 'sale')
    const best = plans.find((p) => p.state === 'reachable') ?? null
    const state = unlocked('sale') || (!hasSale && unlocked('craft')) ? 'unlocked' : best ? 'remaining' : 'unreachable'
    rows.push({
      tpl, name: name || tpl, category: catalog.itemCategoryOf[tpl] ?? OTHER_CATEGORY, plans, state,
      craftUnlocked: state !== 'unlocked' && unlocked('craft'), best: state === 'remaining' ? best : null,
    })
  }
  return rows.sort((a, b) => (a.best?.remaining ?? Infinity) - (b.best?.remaining ?? Infinity) || a.name.localeCompare(b.name))
}

// ---- 칩·검색·카테고리 ----

export type UnlockChip = 'all' | 'sale' | 'craft' | 'unlocked' | 'unreachable'
export const UNLOCK_CHIPS: readonly UnlockChip[] = ['all', 'sale', 'craft', 'unlocked', 'unreachable']

function inChip(r: UnlockRow, chip: UnlockChip): boolean {
  if (chip === 'unlocked' || chip === 'unreachable') return r.state === chip
  if (r.state !== 'remaining') return false
  return chip === 'all' || r.plans.some((p) => p.state === 'reachable' && p.source.kind === chip)
}

/** category 가 null 이 아니면 그 카테고리 행만. 검색은 아이템 이름(공백·대소문자 무시). */
export function filterUnlockRows(rows: UnlockRow[], chip: UnlockChip, query: string, category: string | null = null): UnlockRow[] {
  const q = searchKey(query)
  return rows.filter((r) => inChip(r, chip) && (category === null || r.category === category) && (q === '' || searchKey(r.name).includes(q)))
}

export function unlockCounts(rows: UnlockRow[], category: string | null = null): Record<UnlockChip, number> {
  return Object.fromEntries(UNLOCK_CHIPS.map((c) => [c, filterUnlockRows(rows, c, '', category).length])) as Record<UnlockChip, number>
}
