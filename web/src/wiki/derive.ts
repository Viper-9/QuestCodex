import type { Catalog, CatalogQuest, CatalogTrader } from '../api/catalog'

// ---- 필터 상태 (스펙 §3.3) ----
export interface Chips {
  vanilla: boolean
  mod: boolean
  bear: boolean
  usec: boolean
}
export type ChipKey = keyof Chips
export const DEFAULT_CHIPS: Chips = { vanilla: true, mod: true, bear: false, usec: false }

export interface WikiFilters {
  traders: ReadonlySet<string>   // 비어 있으면 전체
  chips: Chips
  query: string
}

// ---- 상인 줄 (§1.2 a) ----
/** 바닐라 상인은 카탈로그(객체 키) 순서, 그 뒤 모드 상인 이름순. */
export function orderTraders(traders: Record<string, CatalogTrader>): CatalogTrader[] {
  const all = Object.values(traders)
  const vanilla = all.filter((t) => t.isVanilla)
  const mods = all.filter((t) => !t.isVanilla).sort((a, b) => a.name.localeCompare(b.name))
  return [...vanilla, ...mods]
}

/** 상인별 퀘스트 수. 필터와 무관한 고정값. */
export function countByTrader(quests: CatalogQuest[]): Record<string, number> {
  const out: Record<string, number> = {}
  for (const q of quests) out[q.traderId] = (out[q.traderId] ?? 0) + 1
  return out
}

/** 아바타 폴백용 이니셜: 두 단어 이상이면 두 단어 첫 글자, 아니면 첫 글자. */
export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)
  if (words.length >= 2) return (words[0].charAt(0) + words[1].charAt(0)).toUpperCase()
  return (words[0] ?? '').charAt(0).toUpperCase()
}

// ---- 리스트 (§3.2) ----
export function filterQuests(quests: CatalogQuest[], f: WikiFilters): CatalogQuest[] {
  const q = f.query.trim().toLowerCase()
  return quests.filter((x) => {
    if (f.traders.size > 0 && !f.traders.has(x.traderId)) return false
    if (x.isVanilla ? !f.chips.vanilla : !f.chips.mod) return false
    if (f.chips.bear || f.chips.usec) {
      if (x.factionOnly === null) return false
      if (x.factionOnly === 'bear' && !f.chips.bear) return false
      if (x.factionOnly === 'usec' && !f.chips.usec) return false
    }
    if (q !== '' && !x.name.toLowerCase().includes(q)) return false
    return true
  })
}

/** 목록 정렬 기준. 도구 줄의 드롭다운 값이자 `sortQuests` 의 인자. */
export type SortKey = 'level' | 'name' | 'chain'
export const DEFAULT_SORT: SortKey = 'level'

/**
 * 연계순의 갈림길 비교자. 레벨 없음을 **0** 으로 친다 — `sortQuests('level')` 의 `Infinity` 와 정반대다.
 *
 * 같은 `minLevel: null` 이지만 두 정렬이 묻는 게 다르다. 레벨순은 "레벨이 몇인가"를 묻고 `null` 은
 * 답이 없으니 뒤로 보낸다. 연계순은 "지금 할 수 있나"를 묻고, 레벨 조건이 아예 없는 퀘스트는
 * **1레벨부터 할 수 있다**는 뜻이라 앞이다. 이걸 `Infinity` 로 통일하면 `사격 연습`·`데뷔` 같은
 * 초반 퀘스트가 전부 체인 뒤로 밀린다(실측: 프라퍼 첫 퀘스트가 10번째로).
 */
const byBranch = (a: CatalogQuest, b: CatalogQuest) =>
  (a.minLevel ?? 0) - (b.minLevel ?? 0) || a.name.localeCompare(b.name)

/**
 * 선행 관계를 따라 `questId → 순위` 를 매긴다. **필터 이전의 카탈로그 전체**로 부를 것 —
 * 보이는 것만으로 계산하면 필터를 건드릴 때마다 순서가 흔들린다.
 *
 * 루트(선행 없음)에서 깊이 우선으로 내려가되, **선행이 전부 배출된 퀘스트만** 방문한다.
 * 그래서 "퀘스트가 자기 선행보다 위에 오는" 일이 구조적으로 불가능하다. 어느 갈래부터 갈지,
 * 어느 루트부터 시작할지는 `byBranch`(레벨 → 이름) 가 정한다 — 선행 관계는 부분 순서라
 * 직접 연결된 쌍의 앞뒤만 정해 주고, 나머지는 이 비교자가 채운다.
 *
 * 순환이 있으면 루트 순회만으로는 다 못 채우므로, 남은 것 중 가장 앞선 하나를 선행 검사 없이
 * 밀어 넣고 다시 이어 간다. 매 회 최소 1개가 배출되니 반드시 끝난다. 현재 카탈로그(591개)에는
 * 순환이 없어 이 분기는 타지 않지만, 모드가 서로를 선행으로 걸면 무한 루프 대신 여기로 온다.
 */
export function chainRank(quests: CatalogQuest[]): ReadonlyMap<string, number> {
  const byId = new Map(quests.map((q) => [q.id, q]))
  /** 카탈로그에 없는 선행(`resolved: false`)과 자기참조는 버린다 — 영영 만족될 수 없어 전부 순환 취급된다. */
  const prereqs = new Map(quests.map((q) =>
    [q.id, [...new Set(q.prerequisites)].filter((p) => p !== q.id && byId.has(p))]))
  const children = new Map(quests.map((q) => [q.id, [] as CatalogQuest[]]))
  for (const q of quests) for (const p of prereqs.get(q.id)!) children.get(p)!.push(q)

  const rank = new Map<string, number>()
  const ready = (q: CatalogQuest) => !rank.has(q.id) && prereqs.get(q.id)!.every((p) => rank.has(p))
  /** 배출은 여기 한 곳 — 선행 검사는 자식으로 내려갈 때만 한다(인자로 받은 노드는 호출부가 책임). */
  const emit = (q: CatalogQuest) => {
    rank.set(q.id, rank.size)
    for (const child of [...children.get(q.id)!].sort(byBranch)) if (ready(child)) emit(child)
  }

  for (const root of quests.filter((q) => prereqs.get(q.id)!.length === 0).sort(byBranch)) {
    if (ready(root)) emit(root)
  }
  while (rank.size < quests.length) {
    const rest = quests.filter((q) => !rank.has(q.id)).sort(byBranch)
    emit(rest.find(ready) ?? rest[0])          // find 가 빈손이면 전부 순환 — 가장 앞선 것을 강제 배출
  }
  return rank
}

/**
 * 기준에 따라 정렬한다. 입력은 바꾸지 않는다.
 *
 * - `level`: 레벨 오름차순. `minLevel: null`(레벨 제한 자체가 없는 퀘스트)은 `Infinity` 로 쳐서
 *   **맨 뒤**로 보낸다 — 0 으로 치면 레벨 1 퀘스트보다 앞서 목록 머리를 차지한다. 동점은 이름순.
 * - `name`: 레벨을 무시하고 이름순.
 * - `chain`: `chainRank` 가 매긴 순위대로. 순위는 필터 이전 카탈로그 전체로 계산된 것이라,
 *   걸러진 목록에 적용해도 남은 퀘스트끼리의 앞뒤가 그대로 유지된다. `rank` 가 없으면 레벨순으로
 *   폴백하고, 순위에 없는 퀘스트는 뒤로 보내 `byBranch` 로 줄세운다.
 */
export function sortQuests(
  quests: CatalogQuest[],
  sort: SortKey = DEFAULT_SORT,
  rank?: ReadonlyMap<string, number>,
): CatalogQuest[] {
  const byName = (a: CatalogQuest, b: CatalogQuest) => a.name.localeCompare(b.name)
  if (sort === 'name') return [...quests].sort(byName)
  if (sort === 'chain' && rank) {
    return [...quests].sort((a, b) =>
      (rank.get(a.id) ?? Infinity) - (rank.get(b.id) ?? Infinity) || byBranch(a, b))
  }
  return [...quests].sort((a, b) => (a.minLevel ?? Infinity) - (b.minLevel ?? Infinity) || byName(a, b))
}

// ---- 모드 태그 색 (스펙 §5) ----
/** 팔레트 색 개수. `styles.css` 의 `--mod-1` … `--mod-N` 토큰 수와 반드시 같아야 한다. */
export const MOD_COLOR_COUNT = 6

/**
 * 카탈로그에 등장하는 모드 이름을 이름순으로 정렬해 팔레트 색을 1부터 순서대로 배정한다.
 * 반환값은 `모드 이름 → 색 번호(1..MOD_COLOR_COUNT)`.
 *
 * 필터가 아니라 **카탈로그 전체**를 넣어야 한다 — 보이는 목록으로 계산하면 검색·필터를 할 때마다
 * 같은 모드의 색이 바뀐다. 모드 수가 팔레트보다 많으면 색이 순환해 재사용된다(중복 허용).
 */
export function assignModColors(quests: CatalogQuest[]): Record<string, number> {
  const names = [...new Set(quests.map((q) => q.modName).filter((n): n is string => n !== null))]
  names.sort((a, b) => a.localeCompare(b))
  const out: Record<string, number> = {}
  names.forEach((name, i) => { out[name] = (i % MOD_COLOR_COUNT) + 1 })
  return out
}

// ---- 이름 조회 (§4.3) ----
export interface NameLookup {
  traderName(id: string): string
  questName(id: string): string | undefined
}

export function makeLookup(catalog: Catalog): NameLookup {
  return {
    traderName: (id) => catalog.traders[id]?.name ?? id,
    questName: (id) => catalog.quests[id]?.name,
  }
}

// ---- Set 토글 (상인 다중 선택, Task 5 의 펼침 상태와 공용) ----
/** id 가 없으면 추가, 있으면 제거한 새 Set 을 돌려준다. 입력 Set 은 바꾸지 않는다. */
export function toggleMember<T>(set: ReadonlySet<T>, id: T): Set<T> {
  const next = new Set(set)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  return next
}
