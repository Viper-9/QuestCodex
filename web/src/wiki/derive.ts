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
  mods: ReadonlySet<string>      // 출처 모드(modKey). 비어 있거나 Mod 칩이 꺼져 있으면 무시
}

// ---- 출처 모드 ----
/** 출처를 못 찾은 모드 퀘스트(C# 코드로 주입 등)의 키. 실제 모드 폴더명은 빈 문자열일 수 없다. */
export const UNKNOWN_MOD = ''

/**
 * 모드가 덮어쓴 바닐라 퀘스트의 키 앞머리. 같은 모드가 퀘스트를 추가도 하고 덮어쓰기도 하면 칩이 두 개로 나뉜다.
 * ':' 은 윈도우 폴더명에 쓸 수 없어 실제 모드 폴더명(추가 퀘스트의 키)과 겹치지 않는다.
 */
export const OVERRIDE_PREFIX = ':'

/** 퀘스트의 출처 모드 키. 모드인데 출처 미상이면 UNKNOWN_MOD, 모드가 덮어쓴 바닐라면 OVERRIDE_PREFIX+모드명, 그 밖의 바닐라는 null. */
export function modKey(q: CatalogQuest): string | null {
  if (q.isVanilla) return q.overriddenBy ? OVERRIDE_PREFIX + q.overriddenBy : null
  return q.modName ?? UNKNOWN_MOD
}

/** 목록 태그·칩 점에 쓸 모드 이름. 모드 퀘스트는 출처 모드, 덮어쓴 바닐라는 덮어쓴 모드. */
export function sourceModName(q: CatalogQuest): string | null {
  return q.isVanilla ? (q.overriddenBy ?? null) : q.modName
}

/** 출처 태그 색 번호. 바닐라 그대로거나 출처 미상이면 undefined → 기본 --mod 색. */
export function modColorOf(q: CatalogQuest, colors: Record<string, number>): number | undefined {
  const name = sourceModName(q)
  return name ? colors[name] : undefined
}

export interface ModEntry {
  key: string
  /** 칩에 보일 모드 이름. 출처 미상이면 UNKNOWN_MOD */
  name: string
  /** true 면 이 모드가 덮어쓴 바닐라 퀘스트 묶음 */
  overridden: boolean
  count: number
}

/** 모드 칩 줄의 항목. 모드 이름순(같은 모드면 추가 → 덮어쓰기), 출처 미상은 맨 뒤. 개수는 필터와 무관한 고정값. */
export function listMods(quests: CatalogQuest[]): ModEntry[] {
  const counts = new Map<string, number>()
  for (const q of quests) {
    const k = modKey(q)
    if (k !== null) counts.set(k, (counts.get(k) ?? 0) + 1)
  }
  return [...counts]
    .map(([key, count]) => {
      const overridden = key.startsWith(OVERRIDE_PREFIX)
      return { key, name: overridden ? key.slice(OVERRIDE_PREFIX.length) : key, overridden, count }
    })
    .sort((a, b) =>
      Number(a.key === UNKNOWN_MOD) - Number(b.key === UNKNOWN_MOD)
      || a.name.localeCompare(b.name)
      || Number(a.overridden) - Number(b.overridden))
}

/** 모드 선택이 걸린 상태면 그 모드들의 퀘스트만, 아니면 그대로. 목록 필터와 상인 개수가 같이 쓴다. */
export function inMods(quests: CatalogQuest[], mods: ReadonlySet<string>): CatalogQuest[] {
  if (mods.size === 0) return quests
  return quests.filter((q) => {
    const k = modKey(q)
    return k !== null && mods.has(k)
  })
}

// ---- 상인 줄 (§1.2 a) ----
/** 바닐라 상인은 카탈로그(객체 키) 순서, 그 뒤 모드 상인 이름순. */
export function orderTraders(traders: Record<string, CatalogTrader>): CatalogTrader[] {
  const all = Object.values(traders)
  const vanilla = all.filter((t) => t.isVanilla)
  const mods = all.filter((t) => !t.isVanilla).sort((a, b) => a.name.localeCompare(b.name))
  return [...vanilla, ...mods]
}

/** 상인별 퀘스트 수. 모드 선택(`inMods`)만 반영하고 나머지 필터와는 무관하다. */
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
  const source = f.chips.mod ? inMods(quests, f.mods) : quests
  return source.filter((x) => {
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
export const DEFAULT_SORT: SortKey = 'chain'

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
export const MOD_COLOR_COUNT = 10

/**
 * 카탈로그에 등장하는 모드 이름을 이름순으로 정렬해 팔레트 색을 1부터 순서대로 배정한다.
 * 반환값은 `모드 이름 → 색 번호(1..MOD_COLOR_COUNT)`.
 *
 * 필터가 아니라 **카탈로그 전체**를 넣어야 한다 — 보이는 목록으로 계산하면 검색·필터를 할 때마다
 * 같은 모드의 색이 바뀐다. 모드 수가 팔레트보다 많으면 색이 순환해 재사용된다(중복 허용).
 */
export function assignModColors(quests: CatalogQuest[]): Record<string, number> {
  const names = [...new Set(quests.map(sourceModName).filter((n): n is string => n !== null))]
  names.sort((a, b) => a.localeCompare(b))
  const out: Record<string, number> = {}
  names.forEach((name, i) => { out[name] = (i % MOD_COLOR_COUNT) + 1 })
  return out
}

// ---- 배타 분기 (conditions.Fail 의 Quest 조건) ----
export interface BranchInfo {
  /** 이 퀘스트를 완료하면 실패하는 퀘스트 */
  failsOnComplete: string[]
  /** 완료하면 이 퀘스트를 실패시키는 퀘스트 중 failsOnComplete 에 없는 것 — 한쪽 방향 분기 */
  failedBy: string[]
  /** failsOnComplete 를 완료로만 요구해서 함께 막히는 후속 (연쇄) */
  blocked: string[]
  /** failsOnComplete 의 실패를 요구해서 대신 열리는 퀘스트 */
  opened: string[]
}

const needsSuccessOnly = (s: string[]) => s.includes('Success') && !s.includes('Fail')
const needsFailOnly = (s: string[]) => s.includes('Fail') && !s.includes('Success')

/**
 * 분기가 걸린 퀘스트만 담은 `questId → BranchInfo`. **카탈로그 전체**로 한 번 계산한다.
 *
 * `failsWhen` 은 "저게 완료되면 내가 실패" 방향이라, 역인덱스를 만들어 "내가 완료되면 저게 실패" 를 얻는다.
 * 막히는 후속은 실패한 퀘스트를 `Success` 로만 요구하는 퀘스트를 연쇄로 따라간다 — `Success/Fail` 둘 다 받거나
 * `Started` 만 요구하면 실패해도 열리므로 제외한다. 대신 열리는 퀘스트는 직접 실패한 것 기준 한 단계만 본다.
 */
export function branchIndex(quests: CatalogQuest[]): ReadonlyMap<string, BranchInfo> {
  const byId = new Map(quests.map((q) => [q.id, q]))
  const byName = (a: string, b: string) => (byId.get(a)?.name ?? a).localeCompare(byId.get(b)?.name ?? b)

  const failsOnComplete = new Map<string, Set<string>>()
  for (const q of quests) {
    for (const f of q.failsWhen) {
      if (!f.statuses.includes('Success')) continue
      if (!failsOnComplete.has(f.questId)) failsOnComplete.set(f.questId, new Set())
      failsOnComplete.get(f.questId)!.add(q.id)
    }
  }

  /** 선행 questId → 그 선행을 needStatuses 로 요구하는 퀘스트들 */
  const dependents = new Map<string, { id: string; needStatuses: string[] }[]>()
  for (const q of quests) {
    for (const r of q.requirements) {
      if (r.kind !== 'quest') continue
      if (!dependents.has(r.questId)) dependents.set(r.questId, [])
      dependents.get(r.questId)!.push({ id: q.id, needStatuses: r.needStatuses })
    }
  }

  const out = new Map<string, BranchInfo>()
  const ids = new Set([...failsOnComplete.keys(), ...quests.filter((q) => q.failsWhen.length > 0).map((q) => q.id)])
  for (const id of ids) {
    if (!byId.has(id)) continue
    const failed = failsOnComplete.get(id) ?? new Set<string>()
    const failedBy = (byId.get(id)!.failsWhen)
      .filter((f) => f.statuses.includes('Success') && !failed.has(f.questId))
      .map((f) => f.questId)
    if (failed.size === 0 && failedBy.length === 0) continue

    const lost = new Set([id, ...failed])
    const blocked: string[] = []
    const queue = [...failed]
    while (queue.length > 0) {
      for (const d of dependents.get(queue.shift()!) ?? []) {
        if (lost.has(d.id) || !needsSuccessOnly(d.needStatuses)) continue
        lost.add(d.id)
        blocked.push(d.id)
        queue.push(d.id)
      }
    }
    const opened = [...new Set([...failed].flatMap((f) =>
      (dependents.get(f) ?? []).filter((d) => needsFailOnly(d.needStatuses) && !lost.has(d.id)).map((d) => d.id)))]

    out.set(id, {
      failsOnComplete: [...failed].sort(byName),
      failedBy: failedBy.sort(byName),
      blocked: blocked.sort(byName),
      opened: opened.sort(byName),
    })
  }
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

/**
 * 퀘스트마다 `unlocks` 를 끝까지 따라가 중복 없이 센 후속 수(자신 제외). Forge 댓글 요청 — 뒤를 많이 막고 있는
 * 퀘스트(관문)와 막다른 퀘스트를 숫자로 구분한다. 위키는 전부 세고(전체 수), 진행현황은 `keep` 에 아직 남은
 * 퀘스트만 참으로 넘긴다(남은 수). `keep` 이 거짓인 퀘스트도 그 너머는 계속 따라간다 — 이미 깬 퀘스트 뒤에도
 * 남은 퀘스트가 있을 수 있다. 카탈로그에 없는 id 는 버리고, 순환은 방문 집합이 막는다.
 */
export function followupCounts(quests: CatalogQuest[], keep: (id: string) => boolean = () => true): Map<string, number> {
  const byId = new Map(quests.map((q) => [q.id, q]))
  const out = new Map<string, number>()
  for (const q of quests) {
    const seen = new Set<string>([q.id])
    const stack = [...q.unlocks]
    let n = 0
    while (stack.length) {
      const id = stack.pop()!
      if (seen.has(id)) continue
      seen.add(id)
      const next = byId.get(id)
      if (!next) continue
      if (keep(id)) n++
      stack.push(...next.unlocks)
    }
    out.set(q.id, n)
  }
  return out
}

/** 후속 많은순(most) / 적은순(least). 같으면 들어온 순서(탭별 기본 정렬)를 지킨다 */
export function sortByFollowups(rows: CatalogQuest[], counts: ReadonlyMap<string, number>, dir: 'most' | 'least' = 'most'): CatalogQuest[] {
  const sign = dir === 'most' ? -1 : 1
  return [...rows].sort((a, b) => sign * ((counts.get(a.id) ?? 0) - (counts.get(b.id) ?? 0)))
}
