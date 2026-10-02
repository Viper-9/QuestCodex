import type { Catalog, CatalogQuest, ItemRef, Objective, ObjectivePrep, PrepItem } from '../api/catalog'
import type { Holding, ObjectiveProgress, ProfileProgress, QuestProgress, QuestStatus } from '../api/progress'
import { inMods } from '../wiki/derive'

// 진행현황 화면의 파생값. wiki/derive.ts 와 같은 규칙 — React·DOM 없이 인자만으로 계산해 vitest 로 검증한다.

// ---- 퀘스트 상태 ----

const LOCKED: QuestProgress = { status: 'Locked', startTime: null, finishTime: null, lockReasons: [], objectives: {} }

/** 프로필에 없는 퀘스트는 서버 규약대로 Locked */
export function questProgress(p: ProfileProgress, questId: string): QuestProgress {
  return p.quests[questId] ?? LOCKED
}

/** 더 진행할 수 없는 상태. 이 퀘스트의 남은 목표는 아무것도 요구하지 않는다. */
const CLOSED = new Set<QuestStatus>(['Success', 'Fail', 'MarkedAsFailed', 'Expired'])

export function isClosed(status: QuestStatus): boolean {
  return CLOSED.has(status)
}

/**
 * 영영 열리지 않는 잠김 — 다른 진영 전용이거나, 선행이 이미 끝났는데 다른 결과를 요구하는 경우(택일 분기:
 * "실패해야 열림" 인데 이미 완료). 필요 아이템 합산에서 빼지 않으면 못 할 퀘스트의 아이템까지 모으라고 한다.
 */
export function isUnreachable(qp: QuestProgress): boolean {
  return qp.lockReasons.some((r) =>
    r.kind === 'faction'
    || (r.kind === 'quest' && isClosed(r.currentStatus) && !r.needStatuses.includes(r.currentStatus)))
}

/** 아이템을 모을 가치가 있는 퀘스트: 끝나지 않았고 도달할 수 있다 */
export function isPending(qp: QuestProgress): boolean {
  return !isClosed(qp.status) && !isUnreachable(qp)
}

/** 목표의 남은 수 = max(0, target − current). 카운터가 없으면(프로필에 없는 퀘스트) 0 부터 센다. */
export function remaining(o: Objective, op: ObjectiveProgress | undefined): number {
  if (op?.done) return 0
  const target = op?.target ?? o.prep?.item?.count ?? o.targetCount ?? 0
  return Math.max(0, target - (op?.current ?? 0))
}

// ---- 아이템 합산 ----

/**
 * 합산 단위. 대체 목록(그중 하나면 되는 아이템들)은 tpl 집합 전체가 하나의 단위다 — 광신도 2부처럼 같은 MS2000 을
 * 목표 3개로 나눠 요구하면 셋이 한 줄로 합쳐지고, "도그태그 BEAR 아무거나" 는 변종 tpl 을 묶은 한 줄이 된다.
 */
export function itemKey(item: PrepItem): string {
  return [...new Set(item.items.map((i) => i.tpl))].sort().join('|')
}

export function holdingOf(key: string, inventory: Record<string, Holding>): Holding {
  let count = 0
  let fir = 0
  for (const tpl of key.split('|')) {
    count += inventory[tpl]?.count ?? 0
    fir += inventory[tpl]?.fir ?? 0
  }
  return { count, fir }
}

export interface NeedSource {
  questId: string
  conditionId: string
  action: PrepItem['action']
  count: number
  fir: boolean
}

export interface NeedRow {
  key: string
  items: ItemRef[]
  /** 남은 수 합계(FIR 요구분 포함) */
  need: number
  /** 그중 FIR 이어야 하는 수 */
  needFir: number
  have: number
  haveFir: number
  sources: NeedSource[]
}

export interface ItemNeed {
  quest: CatalogQuest
  objective: Objective & { prep: ObjectivePrep & { item: PrepItem } }
  count: number
}

/** 제출·설치 목표 중 남은 수가 있는 것. `accept` 로 퀘스트를 거른다(기본: 끝나지 않고 도달 가능한 전부). */
export function itemNeeds(
  catalog: Catalog,
  progress: ProfileProgress,
  accept: (qp: QuestProgress) => boolean = isPending,
): ItemNeed[] {
  const out: ItemNeed[] = []
  for (const quest of Object.values(catalog.quests)) {
    const qp = questProgress(progress, quest.id)
    if (!accept(qp)) continue
    for (const o of quest.objectives) {
      if (!o.prep?.item) continue
      const count = remaining(o, qp.objectives[o.conditionId])
      if (count > 0) out.push({ quest, objective: o as ItemNeed['objective'], count })
    }
  }
  return out
}

/** 같은 합산 단위의 남은 수를 **전부 더한 뒤** 보유 수와 붙인다. 순서는 입력 순서(처음 나온 단위 순). */
export function aggregateNeeds(needs: ItemNeed[], inventory: Record<string, Holding>): NeedRow[] {
  const rows = new Map<string, NeedRow>()
  for (const { quest, objective, count } of needs) {
    const item = objective.prep.item
    const key = itemKey(item)
    let row = rows.get(key)
    if (!row) {
      const h = holdingOf(key, inventory)
      row = { key, items: item.items, need: 0, needFir: 0, have: h.count, haveFir: h.fir, sources: [] }
      rows.set(key, row)
    }
    row.need += count
    if (item.foundInRaid) row.needFir += count
    row.sources.push({ questId: quest.id, conditionId: objective.conditionId, action: item.action, count, fir: item.foundInRaid })
  }
  return [...rows.values()]
}

/**
 * 부족한 수. FIR 요구분은 FIR 보유로만 채우고, 나머지는 남은 보유(FIR 포함)로 채운다.
 * 예) 필요 5(그중 FIR 3), 보유 4(그중 FIR 1) → FIR 2 부족 + 일반 2 는 남은 보유 3 으로 충분 = 2.
 */
export function missing(row: Pick<NeedRow, 'need' | 'needFir' | 'have' | 'haveFir'>): number {
  const firUsed = Math.min(row.haveFir, row.needFir)
  const firShort = row.needFir - firUsed
  const anyShort = Math.max(0, (row.need - row.needFir) - (row.have - firUsed))
  return firShort + anyShort
}

/** 같은 퀘스트·같은 행동·같은 FIR 여부의 목표를 한 줄로 합친다(예: 설치 ×1 네 개 → 설치 ×4). 처음 나온 순서 유지. */
export function mergeSources(sources: NeedSource[]): NeedSource[] {
  const merged = new Map<string, NeedSource>()
  for (const s of sources) {
    const key = `${s.questId}|${s.action}|${s.fir}`
    const prev = merged.get(key)
    if (prev) prev.count += s.count
    else merged.set(key, { ...s })
  }
  return [...merged.values()]
}

/** 부족분 많은 순 → 필요 많은 순 → 이름순 */
export function sortNeedRows(rows: NeedRow[]): NeedRow[] {
  return [...rows].sort((a, b) =>
    missing(b) - missing(a) || b.need - a.need || itemLabel(a).localeCompare(itemLabel(b)))
}

/** 줄 이름: 대체 목록이면 첫 이름 (화면에서 "외 N종" 을 붙인다) */
export function itemLabel(row: Pick<NeedRow, 'items'>): string {
  return row.items[0]?.name ?? ''
}

export type ItemFilter = 'all' | 'missing' | 'owned' | 'fir'
export const ITEM_FILTERS: readonly ItemFilter[] = ['all', 'missing', 'owned', 'fir']

/** 검색은 공백·대소문자 무시 — 게임 이름이 "T H I C C Item case" 처럼 띄어 쓰인 경우가 있다. */
export function searchKey(s: string): string {
  return s.toLowerCase().replace(/\s+/g, '')
}

/** 핸드북에 없는 아이템(모드 등)의 카테고리 */
export const OTHER_CATEGORY = 'other'

/** 행의 카테고리. 대안 아이템 중 카테고리를 아는 첫 것을 따른다. */
export function rowCategory(row: Pick<NeedRow, 'items'>, categoryOf: Record<string, string>): string {
  for (const i of row.items) {
    const c = categoryOf[i.tpl]
    if (c) return c
  }
  return OTHER_CATEGORY
}

/** category 가 null 이 아니면 그 카테고리 행만. */
export function filterNeedRows(
  rows: NeedRow[], filter: ItemFilter, query: string,
  category: string | null = null, categoryOf: Record<string, string> = {},
): NeedRow[] {
  const q = searchKey(query)
  return rows.filter((r) => {
    if (category !== null && rowCategory(r, categoryOf) !== category) return false
    if (filter === 'missing' && missing(r) === 0) return false
    if (filter === 'owned' && r.have === 0) return false
    if (filter === 'fir' && r.needFir === 0) return false
    return q === '' || r.items.some((i) => searchKey(i.name).includes(q))
  })
}

// ---- 레이드 준비 ----

/** 맵 탭 순서. 키는 feature/quest-map 과 같은 locations 폴더 키 — 표시 이름은 i18n map.name.<key> */
export const MAP_ORDER: readonly string[] = [
  'bigmap', 'factory4_day', 'woods', 'shoreline', 'interchange', 'rezervbase',
  'lighthouse', 'tarkovstreets', 'sandbox', 'laboratory', 'labyrinth',
]

/** 같은 맵의 변종(주간·야간 공장, 그라운드 제로 저레벨·고레벨, 시작 구역)은 한 탭 */
const MAP_GROUP: Record<string, string> = {
  factory4_night: 'factory4_day',
  sandbox_high: 'sandbox',
  sandbox_start: 'sandbox',
  laboratory_dark: 'laboratory',
}

/** 개발용 로케이션. 실제 맵과 함께 나열된 조건에만 나와서(리저브 + develop) 버려도 잃는 게 없다 */
const IGNORED_MAPS = new Set(['develop'])

export function mapGroup(key: string): string | null {
  const k = key.toLowerCase()
  return IGNORED_MAPS.has(k) ? null : MAP_GROUP[k] ?? k
}

/** 레이드 안에서 하는 목표. 제출(상인)·레벨·스킬·찾기(FIR 은 필요 아이템 화면) 같은 것은 브리핑에 넣지 않는다. */
const RAID_TYPES = new Set(['CounterCreator', 'LeaveItemAtLocation', 'PlaceBeacon', 'VisitPlace'])

export function isRaidObjective(o: Objective): boolean {
  return RAID_TYPES.has(o.conditionType)
}

/**
 * 목표를 할 수 있는 맵(탭 키). 빈 배열 = 어느 맵이든.
 * 목표에 맵 조건이 없으면 퀘스트의 맵을 쓴다 — 표식 설치·존 방문은 조건에 맵이 없고 퀘스트에만 묶여 있다(인게임 맵 정렬과 같은 기준).
 */
export function objectiveMaps(q: CatalogQuest, o: Objective): string[] {
  const keys = o.prep?.mapKeys ?? []
  const src = keys.length > 0 ? keys : q.locationKey ? [q.locationKey] : []
  const out: string[] = []
  for (const k of src) {
    const g = mapGroup(k)
    if (g !== null && !out.includes(g)) out.push(g)
  }
  return out
}

/**
 * 목표 문장 속 맵 이름 → 탭 키. 영어는 대소문자·단어 경계를 지켜 일반 명사("reserve", "factory")와 구분하고,
 * 한국어는 게임 표기 그대로(해안선·해안가 둘 다 쓰인다). "Lab" 은 "Lab journal" 같은 아이템 이름과 겹쳐 "The Lab"·"Laboratory" 만.
 */
const MAP_TEXT: readonly [string, RegExp][] = [
  ['bigmap', /\bCustoms\b|세관/],
  ['factory4_day', /\bFactory\b|공장/],
  ['woods', /\bWoods\b|삼림/],
  ['shoreline', /\bShoreline\b|해안선|해안가/],
  ['interchange', /\bInterchange\b|인터체인지/],
  ['rezervbase', /\bReserve\b|리저브/],
  ['lighthouse', /\bLighthouse\b|등대/],
  ['tarkovstreets', /\bStreets of Tarkov\b|타르코프 시내/],
  ['sandbox', /\bGround Zero\b|그라운드 제로/],
  ['laboratory', /\bThe Lab\b|\bLaboratory\b|연구소/],
  ['labyrinth', /\bLabyrinth\b/],
]

/** 문장에 나온 맵(MAP_ORDER 순). 데이터에 맵이 없는 목표에만 쓴다 — 문장은 언어·모드마다 달라 데이터보다 약한 근거다. */
export function mapsFromText(text: string): string[] {
  return MAP_TEXT.filter(([, re]) => re.test(text)).map(([key]) => key)
}

export interface RaidEntry {
  quest: CatalogQuest
  objective: Objective
  progress: ObjectiveProgress | undefined
  /** 빈 배열 = 어느 맵이든 · 맵 미상 */
  maps: string[]
  /** maps 를 데이터가 아니라 목표 문장에서 추정했다(광신도 2부 "삼림(Woods)에…", Hobby Club "…on Customs") */
  inferred: boolean
}

/** 진행 중 퀘스트의 끝나지 않은 레이드 목표 전부 */
export function raidEntries(catalog: Catalog, progress: ProfileProgress): RaidEntry[] {
  const out: RaidEntry[] = []
  for (const quest of Object.values(catalog.quests)) {
    const qp = questProgress(progress, quest.id)
    if (qp.status !== 'Started') continue
    for (const o of quest.objectives) {
      if (!isRaidObjective(o)) continue
      const op = qp.objectives[o.conditionId]
      if (op?.done) continue
      const known = objectiveMaps(quest, o)
      const guessed = known.length === 0 ? mapsFromText(o.text) : []
      out.push({ quest, objective: o, progress: op, maps: known.length > 0 ? known : guessed, inferred: guessed.length > 0 })
    }
  }
  return out
}

/** 펼친 퀘스트의 목표 한 줄이 지금 탭과 어떤 관계인가: 이 맵 / 다른 맵 / 맵 미상 */
export type EntryPlace = 'here' | 'elsewhere' | 'unknown'

export function entryPlace(e: RaidEntry, map: string): EntryPlace {
  if (e.maps.length === 0) return 'unknown'
  return e.maps.includes(map) ? 'here' : 'elsewhere'
}

export interface QuestGroup {
  quest: CatalogQuest
  entries: RaidEntry[]
}

/** 목표 줄을 퀘스트별로 묶는다. 순서는 퀘스트가 처음 나온 순, 퀘스트 안은 목표 순서 그대로. */
export function groupByQuest(entries: RaidEntry[]): QuestGroup[] {
  const groups = new Map<string, QuestGroup>()
  for (const e of entries) {
    const g = groups.get(e.quest.id)
    if (g) g.entries.push(e)
    else groups.set(e.quest.id, { quest: e.quest, entries: [e] })
  }
  return [...groups.values()]
}

/** 이 목표가 고른 맵의 좌표를 가졌나 — 레이드 지도에 찍히는 목표 */
const onMap = (e: RaidEntry, map: string) => (e.objective.locations ?? []).some((l) => mapGroup(l.map) === map)

/**
 * 카운터 진행률(0~1): target 이 2 이상인 목표들의 합계 기준. 없으면 null.
 * target 1(방문·설치·1회 처치)은 "했다/안 했다" 라 진행률로 치지 않는다.
 */
function counterRatio(entries: RaidEntry[]): number | null {
  let current = 0
  let target = 0
  for (const { progress: p } of entries) {
    if (!p || p.target === null || p.target < 2) continue
    current += Math.min(p.current, p.target)
    target += p.target
  }
  return target > 0 ? current / target : null
}

/**
 * 레이드 브리핑의 퀘스트 순서(사용자 결정): ① 이 맵 지도에 찍히는 퀘스트 ② 카운터가 있는 퀘스트 ③ 나머지.
 * ①②는 안에서 카운터 진행률 높은 순, 같으면(③은 전부) 퀘스트 이름순.
 */
export function orderRaidQuests(entries: RaidEntry[], map: string): QuestGroup[] {
  return groupByQuest(entries)
    .map((g) => {
      const ratio = counterRatio(g.entries)
      const rank = g.entries.some((e) => onMap(e, map)) ? 0 : ratio !== null ? 1 : 2
      return { g, rank, ratio: ratio ?? -1 }
    })
    .sort((a, b) => a.rank - b.rank || b.ratio - a.ratio || a.g.quest.name.localeCompare(b.g.quest.name))
    .map((x) => x.g)
}

/** 레이드 지도에 찍을 것: 퀘스트 → 번호, 번호를 붙인 목표(지도 탭 만들기용, wiki/mapProjection buildNumberedTabs) */
export interface RaidMapPlan {
  numbers: Map<string, number>
  /** 번호 순 퀘스트 — 크게 보기 팝업의 범례 */
  quests: { n: number; quest: CatalogQuest }[]
  items: { n: number; objective: Objective }[]
}

/**
 * 고른 맵의 목표(mapBrief().here) 중 그 맵 좌표가 있는 것만 지도에 찍는다. 번호는 퀘스트 단위 — 목록 순서
 * (orderRaidQuests, 지도에 찍히는 퀘스트가 맨 위)대로 1부터, 좌표 없는 퀘스트는 건너뛴다.
 * 한 퀘스트의 목표들은 같은 번호·색이라 목록 한 줄 ↔ 지도 마커 묶음이 바로 이어진다.
 */
export function raidMapPlan(here: RaidEntry[], map: string): RaidMapPlan {
  const numbers = new Map<string, number>()
  const quests: RaidMapPlan['quests'] = []
  const items: RaidMapPlan['items'] = []
  for (const { quest, entries } of orderRaidQuests(here, map)) {
    const placed = entries.filter((e) => onMap(e, map))
    if (placed.length === 0) continue
    const n = numbers.size + 1
    numbers.set(quest.id, n)
    quests.push({ n, quest })
    for (const e of placed) items.push({ n, objective: e.objective })
  }
  return { numbers, quests, items }
}

/** 장비·특수 조건 한 줄: 라벨(무기·착용·탈출 …) + 값(대안 목록, 문장이면 한 개) */
export interface RuleRow {
  label: string
  names: string[]
}

/**
 * 한 퀘스트의 여러 목표에서 모은 조건 줄 중 같은 것(라벨·값 동일)은 한 번만 — New Day, New Paths 는 목표 4개가
 * 전부 "한 레이드 안에 완료" 라 같은 줄이 4번 나왔다(피드백). 내용이 다른 줄(탈출 조건 여러 개)은 순서대로 남긴다.
 */
export function dedupeRows(rows: RuleRow[]): RuleRow[] {
  const seen = new Set<string>()
  return rows.filter((r) => {
    const key = `${r.label}\u0000${r.names.join('\u0001')}`
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })
}

export interface MapTab {
  key: string
  /** 이 맵에서만 할 수 있는 목표 수 (어느 맵이든 되는 목표는 세지 않는다 — 모든 탭에 같은 수가 더해질 뿐이라) */
  count: number
}

/** 알려진 맵은 항상(0 이어도) 고정 순서로, 모르는 맵(모드 맵)은 목표가 있을 때만 뒤에 이름순 */
export function mapTabs(entries: RaidEntry[]): MapTab[] {
  const counts = new Map<string, number>()
  for (const e of entries) for (const m of e.maps) counts.set(m, (counts.get(m) ?? 0) + 1)
  const extra = [...counts.keys()].filter((k) => !MAP_ORDER.includes(k)).sort()
  return [...MAP_ORDER, ...extra].map((key) => ({ key, count: counts.get(key) ?? 0 }))
}

export interface MapBrief {
  /** 이 맵에서만 */
  here: RaidEntry[]
  /** 어느 맵이든 · 맵 미상 (Transition 퀘스트처럼 데이터에 맵이 없는 것 포함) */
  anywhere: RaidEntry[]
  /** 이 맵에서 설치할 아이템 합산. 제출은 상인에게 하는 것이라 레이드에 챙겨 갈 물건이 아니다. */
  bring: NeedRow[]
  /**
   * 맵이 없는 목표의 설치 아이템. bring 과 합치지 않는다 — 모든 탭에 같은 줄이 붙어 맵별 목록이 흐려진다
   * (실측 fon: 모드 퀘스트의 설치 아이템 8종이 11개 탭 전부에 나왔다).
   */
  bringAnywhere: NeedRow[]
  /** 무기·장비 조건이 있는 목표 */
  gear: RaidEntry[]
  /** 한 레이드 안에 완료·탈출 조건이 있는 목표 */
  special: RaidEntry[]
}

const hasGear = (p: ObjectivePrep) =>
  p.weapons.length > 0 || p.calibers.length > 0 || p.weaponMods.length > 0 || p.equipment.length > 0 || p.forbiddenEquipment.length > 0
const hasSpecial = (p: ObjectivePrep) => p.oneRaid || p.exitStatuses.length > 0 || p.exitName !== null

function plantNeeds(entries: RaidEntry[]): ItemNeed[] {
  const out: ItemNeed[] = []
  for (const e of entries) {
    if (e.objective.prep?.item?.action !== 'plant') continue
    const count = remaining(e.objective, e.progress)
    if (count > 0) out.push({ quest: e.quest, objective: e.objective as ItemNeed['objective'], count })
  }
  return out
}

export function mapBrief(entries: RaidEntry[], map: string, inventory: Record<string, Holding>): MapBrief {
  const here = entries.filter((e) => e.maps.includes(map))
  const anywhere = entries.filter((e) => e.maps.length === 0)
  const all = [...here, ...anywhere]
  return {
    here,
    anywhere,
    bring: aggregateNeeds(plantNeeds(here), inventory),
    bringAnywhere: aggregateNeeds(plantNeeds(anywhere), inventory),
    gear: all.filter((e) => e.objective.prep !== null && hasGear(e.objective.prep)),
    special: all.filter((e) => e.objective.prep !== null && hasSpecial(e.objective.prep)),
  }
}

/** 루블·달러·유로. 돈 제출(교관 등)은 레이드에서 구하는 게 아니라 버는 것이라 구해 올 것에서 뺀다(실측: 유로 87,312 가 맨 위). */
const CURRENCIES = new Set(['5449016a4bdc2d6f028b456f', '5696686a4bdc2da3298b456a', '569668774bdc2da2298b4568'])

/**
 * 레이드에서 구해 올 것 — 진행 중 퀘스트의 남은 제출 아이템 중 보유로 못 채우는 것(합산 기준, 부족분 많은 순).
 * 찾기(FindItem) 목표의 카운터는 쓰지 않는다: 이미 하나를 제출했는데도 찾기 카운터가 0/2 인 경우가 있어(실측 폼생폼사)
 * "제출까지 남은 수 − 보유" 가 실제로 더 구해야 하는 수다. 잠긴 퀘스트까지 넣으면 목록이 수백 줄이라 진행 중만.
 */
export function raidFinds(catalog: Catalog, progress: ProfileProgress, inventory: Record<string, Holding>): NeedRow[] {
  const handovers = itemNeeds(catalog, progress, (qp) => qp.status === 'Started')
    .filter((n) => n.objective.prep.item.action === 'handover' && !n.objective.prep.item.items.every((i) => CURRENCIES.has(i.tpl)))
  return sortNeedRows(aggregateNeeds(handovers, inventory).filter((r) => missing(r) > 0))
}

/**
 * 구해 올 아이템이 나오는 맵 — 요구 퀘스트들의 찾기(FindItem) 목표 문장에서 읽는다("등대(Lighthouse) 지역의 펌프장에서 정보 찾기").
 * 카탈로그의 찾기 목표에는 대상 아이템이 없어 아이템 단위로 짝지을 수 없으므로 퀘스트 단위다 — 한 퀘스트의 찾기 목표가
 * 여러 맵에 걸치면 그 퀘스트 아이템은 그 맵들 모두에 나온다. 빈 배열 = 맵 미상(일반 전리품: 모자·방탄복 등).
 */
export function findMaps(row: NeedRow, catalog: Catalog): string[] {
  const out: string[] = []
  for (const questId of new Set(row.sources.map((s) => s.questId))) {
    for (const o of catalog.quests[questId]?.objectives ?? []) {
      if (o.conditionType !== 'FindItem') continue
      for (const m of mapsFromText(o.text)) if (!out.includes(m)) out.push(m)
    }
  }
  return out
}

export interface PlacedRow {
  row: NeedRow
  maps: string[]
  place: EntryPlace
}

/** 이 맵 → 맵 미상 → 다른 맵 순(각 묶음 안은 입력 순서 = 부족분 순) */
export function placeFinds(rows: NeedRow[], catalog: Catalog, map: string): PlacedRow[] {
  const rank: Record<EntryPlace, number> = { here: 0, unknown: 1, elsewhere: 2 }
  return rows
    .map((row) => {
      const maps = findMaps(row, catalog)
      const place: EntryPlace = maps.length === 0 ? 'unknown' : maps.includes(map) ? 'here' : 'elsewhere'
      return { row, maps, place }
    })
    .sort((a, b) => rank[a.place] - rank[b.place])
}

// ---- 현황 ----

export interface HandoverReady {
  quest: CatalogQuest
  /** 가진 아이템으로 지금 채울 수 있는 제출 목표 수 */
  ready: number
  /** 남은 제출 목표 수 */
  total: number
}

/**
 * 진행 중 퀘스트 중 지금 바로 제출할 수 있는 목표가 있는 것. 판정은 퀘스트 안에서 합산 — 같은 아이템을 목표 여럿으로
 * 나눠 요구하면(광신도 2부) 그 합계를 보유 수와 비교한다. 퀘스트끼리는 합치지 않는다: "이 퀘스트를 지금 끝낼 수 있나" 를 묻는 것이라서.
 */
export function handoverReady(catalog: Catalog, progress: ProfileProgress, inventory: Record<string, Holding>): HandoverReady[] {
  const byQuest = new Map<string, ItemNeed[]>()
  for (const n of itemNeeds(catalog, progress, (qp) => qp.status === 'Started')) {
    if (n.objective.prep.item.action !== 'handover') continue
    const list = byQuest.get(n.quest.id) ?? []
    list.push(n)
    byQuest.set(n.quest.id, list)
  }
  const out: HandoverReady[] = []
  for (const needs of byQuest.values()) {
    const rows = aggregateNeeds(needs, inventory)
    const covered = new Set(rows.filter((r) => missing(r) === 0).map((r) => r.key))
    const ready = needs.filter((n) => covered.has(itemKey(n.objective.prep.item))).length
    if (ready > 0) out.push({ quest: needs[0].quest, ready, total: needs.length })
  }
  return out
}

export type QuestTab = 'active' | 'available' | 'locked' | 'done' | 'failed'
export const QUEST_TABS: readonly QuestTab[] = ['active', 'available', 'locked', 'done', 'failed']

export function questTab(status: QuestStatus): QuestTab {
  switch (status) {
    case 'Started':
    case 'AvailableForFinish':
      return 'active'
    case 'AvailableForStart':
      return 'available'
    case 'Success':
      return 'done'
    case 'Locked':
      return 'locked'
    default:
      return 'failed'   // Fail · FailRestartable · MarkedAsFailed · Expired
  }
}

const NO_MODS: ReadonlySet<string> = new Set()

/** 탭 개수. `mods` 는 위키와 같은 출처 모드 선택 — 비어 있으면 전체 */
export function countTabs(catalog: Catalog, progress: ProfileProgress, mods = NO_MODS): Record<QuestTab, number> {
  const out: Record<QuestTab, number> = { active: 0, available: 0, locked: 0, done: 0, failed: 0 }
  for (const q of inMods(Object.values(catalog.quests), mods)) out[questTab(questProgress(progress, q.id).status)]++
  return out
}

/** 상인 줄 개수: 고른 탭(과 출처 모드)에 속한 퀘스트만 상인별로 센다 (탭을 바꾸면 숫자도 바뀐다) */
export function countTabByTrader(catalog: Catalog, progress: ProfileProgress, tab: QuestTab, mods = NO_MODS): Record<string, number> {
  const out: Record<string, number> = {}
  for (const q of inMods(Object.values(catalog.quests), mods)) {
    if (questTab(questProgress(progress, q.id).status) === tab) out[q.traderId] = (out[q.traderId] ?? 0) + 1
  }
  return out
}

export interface QuestFilter {
  tab: QuestTab
  /** 비어 있으면 전체 (위키 상인 줄과 같은 다중 선택) */
  traderIds: ReadonlySet<string>
  query: string
  /** 출처 모드. 없거나 비어 있으면 전체 */
  mods?: ReadonlySet<string>
}

export function filterProgressQuests(catalog: Catalog, progress: ProfileProgress, f: QuestFilter): CatalogQuest[] {
  const q = searchKey(f.query)
  const rows = inMods(Object.values(catalog.quests), f.mods ?? NO_MODS)
    .filter((x) => questTab(questProgress(progress, x.id).status) === f.tab)
    .filter((x) => f.traderIds.size === 0 || f.traderIds.has(x.traderId))
    .filter((x) => q === '' || searchKey(x.name).includes(q))
  return sortForTab(rows, progress, f.tab)
}

/** 목표 진행률 0~1. 카운터 있는 목표는 current/target, 없는 목표는 끝났으면 1. 목표가 없으면 0 */
export function questCompletion(q: CatalogQuest, qp: QuestProgress): number {
  if (q.objectives.length === 0) return 0
  let sum = 0
  for (const o of q.objectives) {
    const op = qp.objectives[o.conditionId]
    if (op?.done) sum += 1
    else if (op && op.target) sum += Math.min(op.current / op.target, 1)
  }
  return sum / q.objectives.length
}

const ACTIVE = new Set<QuestStatus>(['Started', 'AvailableForFinish'])

/**
 * 잠김 탭의 단계: 0 = 지금 진행 중인 선행 하나만 남음(끝내면 바로 열림), 1 = 그 밖의 사유,
 * 2 = 사유를 모름(프로필에 없는 퀘스트), 3 = 도달 불가(다른 진영·닫힌 택일 분기)
 */
function lockTier(qp: QuestProgress): number {
  if (isUnreachable(qp)) return 3
  const rs = qp.lockReasons
  if (rs.length === 0) return 2
  if (rs.length === 1 && rs[0].kind === 'quest' && ACTIVE.has(rs[0].currentStatus)) return 0
  return 1
}

/** 완료·실패 시각(ms). 없으면 -Infinity 라 최신순에서 맨 뒤 */
function finishedAt(qp: QuestProgress): number {
  const ms = qp.finishTime ? Date.parse(qp.finishTime) : NaN
  return Number.isNaN(ms) ? -Infinity : ms
}

/**
 * 탭별 기본 정렬 — 현황은 "다음에 할 것" 화면이라 탭마다 보고 싶은 게 다르다. 마지막은 늘 최소 레벨 → 이름.
 * 진행 중: 완료 보고 대기 → 진행률 높은 순 / 잠김: lockTier → 남은 사유 수 적은 순 /
 * 완료·실패: 끝난 시각 최신순 / 수락 가능: 최소 레벨
 */
export function sortForTab(quests: CatalogQuest[], progress: ProfileProgress, tab: QuestTab): CatalogQuest[] {
  const byLevel = (a: CatalogQuest, b: CatalogQuest) => (a.minLevel ?? 0) - (b.minLevel ?? 0) || a.name.localeCompare(b.name)
  const key = new Map(quests.map((q): [string, number[]] => {
    const qp = questProgress(progress, q.id)
    switch (tab) {
      case 'active': return [q.id, [qp.status === 'AvailableForFinish' ? 0 : 1, -questCompletion(q, qp)]]
      case 'locked': return [q.id, [lockTier(qp), qp.lockReasons.length]]
      case 'done':
      case 'failed': return [q.id, [-finishedAt(qp)]]
      case 'available': return [q.id, []]
    }
  }))
  return [...quests].sort((a, b) => {
    const ka = key.get(a.id)!
    const kb = key.get(b.id)!
    for (let i = 0; i < ka.length; i++) {
      if (ka[i] !== kb[i]) return ka[i] < kb[i] ? -1 : 1
    }
    return byLevel(a, b)
  })
}

/** 줄 요약용: 첫 번째로 끝나지 않은 목표(카운터 있는 것 우선이 아니라 목표 순서 그대로) */
export function firstOpenObjective(q: CatalogQuest, qp: QuestProgress): Objective | null {
  return q.objectives.find((o) => !qp.objectives[o.conditionId]?.done) ?? null
}

/** 상인별 진행률에 넣을 상인: 퀘스트가 하나라도 있는 상인 */
export function tradersWithQuests(order: string[], progress: ProfileProgress): string[] {
  return order.filter((id) => (progress.traderStats[id]?.total ?? 0) > 0)
}
