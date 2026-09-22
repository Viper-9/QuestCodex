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

/** 레벨 오름차순(null → 0), 같은 레벨은 이름순. 입력은 바꾸지 않는다. */
export function sortQuests(quests: CatalogQuest[]): CatalogQuest[] {
  return [...quests].sort((a, b) => (a.minLevel ?? 0) - (b.minLevel ?? 0) || a.name.localeCompare(b.name))
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
