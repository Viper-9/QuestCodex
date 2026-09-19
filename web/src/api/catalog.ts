// 2단계 스펙 §2.1~2.3 (docs/02-catalog-progress/questcodex-catalog-progress.spec.md) 의 TS 판.
// 서버 JSON 은 camelCase, 판별 필드는 kind.

export interface CatalogTrader {
  id: string
  name: string
  avatarUrl: string | null   // "/files/trader/avatar/<id>.jpg" 전체 경로 또는 null
  isVanilla: boolean
}

export type FactionOnly = 'bear' | 'usec' | null

export type Requirement =
  | { kind: 'quest'; questId: string; needStatuses: string[]; availableAfterSec: number; resolved: boolean }
  | { kind: 'level'; value: number; compare: string }
  | { kind: 'traderLoyalty'; traderId: string; value: number; compare: string }
  | { kind: 'traderStanding'; traderId: string; value: number; compare: string }
  | { kind: 'other'; conditionType: string; text: string }

export interface Objective {
  conditionId: string
  conditionType: string
  text: string
  targetCount: number | null
  optional: boolean
}

export type Reward =
  | { kind: 'item'; tpl: string; name: string; iconUrl: string | null; count: number; categories: string[] }
  | { kind: 'assortUnlock'; traderId: string; tpl: string | null; name: string | null; iconUrl: string | null; loyaltyLevel: number; categories: string[] }
  | { kind: 'production'; areaType: number; tpl: string | null; name: string | null; iconUrl: string | null }
  | { kind: 'traderUnlock'; traderId: string }
  | { kind: 'traderStanding'; traderId: string; value: number }
  | { kind: 'experience'; value: number }
  | { kind: 'skill'; skill: string; value: number }
  | { kind: 'achievement'; achievementId: string }
  | { kind: 'other'; rewardType: string }

export interface QuestRewards {
  started: Reward[]
  success: Reward[]
  fail: Reward[]
}

export interface CatalogQuest {
  id: string
  name: string
  description: string
  traderId: string
  side: string
  factionOnly: FactionOnly
  isVanilla: boolean
  imageUrl: string | null      // 위키에서는 쓰지 않는다 (스펙 §0)
  minLevel: number | null
  requirements: Requirement[]
  prerequisites: string[]
  unlocks: string[]
  objectives: Objective[]
  rewards: QuestRewards
  tags: string[]               // "isolated" | "traderInternal"
}

export interface CatalogWarning {
  questId?: string
  code: string
  detail: string
}

export interface Catalog {
  sptVersion: string
  modVersion: string
  generatedAt: string
  lang: string
  traders: Record<string, CatalogTrader>
  quests: Record<string, CatalogQuest>
  rewardIndex: Record<string, string[]>
  warnings: CatalogWarning[]
}

/** 서버 에러 본문의 error 코드(unknownLang 등), 본문이 없으면 http<status>, 네트워크 실패면 network. */
export class CatalogError extends Error {
  constructor(public readonly code: string, public readonly status: number | null) {
    super(`catalog: ${code}`)
    this.name = 'CatalogError'
  }
}

function isAbort(e: unknown): boolean {
  return e instanceof DOMException && e.name === 'AbortError'
}

export async function fetchCatalog(lang: string, signal?: AbortSignal): Promise<Catalog> {
  let res: Response
  try {
    res = await fetch(`/questcodex/api/catalog?lang=${encodeURIComponent(lang)}`, { signal })
  } catch (e) {
    if (isAbort(e)) throw e
    throw new CatalogError('network', null)
  }
  if (!res.ok) {
    let code = `http${res.status}`
    try {
      const body = (await res.json()) as { error?: unknown }
      if (typeof body.error === 'string') code = body.error
    } catch {
      // 본문이 JSON 이 아님 — http<status> 유지
    }
    throw new CatalogError(code, res.status)
  }
  return (await res.json()) as Catalog
}
