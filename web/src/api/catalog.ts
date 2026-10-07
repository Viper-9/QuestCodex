// 2단계 스펙 §2.1~2.3 (dev-docs/02-catalog-progress/questcodex-catalog-progress.spec.md) 의 TS 판.
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
  | { kind: 'other'; conditionType: string; text: string; targetName: string | null }   // text 가 비면 조건 로케일 없음

export interface ItemRef {
  tpl: string
  /** 로케일·템플릿 어디에도 없으면 tpl 그대로 */
  name: string
}

export interface PrepItem {
  action: 'handover' | 'plant'
  /** 대안 목록 — 그중 하나면 된다 */
  items: ItemRef[]
  count: number
  foundInRaid: boolean
  minDurability: number | null
  maxDurability: number | null
  dogtagLevel: number | null
  plantSeconds: number | null
}

/** 목표 하나의 준비물. 준비할 게 없으면 Objective.prep 이 null. */
export interface ObjectivePrep {
  maps: string[]
  /** 맵 키(소문자, 예: shoreline, factory4_night). 언어와 무관한 대조용. 구버전 서버엔 없다(undefined) */
  mapKeys?: string[]
  item: PrepItem | null
  /** 인정 무기 (OR) */
  weapons: ItemRef[]
  calibers: string[]
  /** [대안, OR][묶음, AND] */
  weaponMods: ItemRef[][]
  /** [슬롯, AND][대안, OR][묶음, AND] */
  equipment: ItemRef[][][]
  forbiddenEquipment: ItemRef[]
  oneRaid: boolean
  exitStatuses: string[]
  exitName: string | null
}

/** Unity 월드 좌표 그대로. y = 높이. SVG 투영은 wiki/mapProjection.ts 가 한다. */
export interface MapPoint {
  x: number
  y: number
  z: number
}

/** map = SPT locations 폴더 키(소문자, 예: bigmap, sandbox_high) */
export interface ObjectiveLocation {
  map: string
  points: MapPoint[]
  /** 구역 처치·신호탄 목표의 영역(08 스펙). 그 밖의 목표는 빈 배열, 필드가 생기기 전 서버는 undefined. */
  areas?: MapArea[]
}

/**
 * 지도 위 사각형 영역. center.x·z 는 영역 중심, center.y 는 존 위치 높이(층 판정용). sizeX·sizeZ 는 월드 x·z 방향 전체 폭(m),
 * yaw 는 수직축 회전(도, Unity 규약 — 위에서 볼 때 양수가 시계 방향). 바닐라는 축 정렬이라 0.
 */
export interface MapArea {
  center: MapPoint
  sizeX: number
  sizeZ: number
  yaw: number
  /**
   * 층 판정용 높이 범위. 구역 처치는 상자 바닥~꼭대기(걸친 층 모두), 신호탄은 서버가 바닥 + 1m 한 점으로 줄여 보낸다.
   * 필드가 생기기 전 서버는 undefined — 그때는 center.y 한 점으로 본다.
   */
  minY?: number
  maxY?: number
}

/**
 * 열쇠가 필요한 문. keyTpl 은 지금 쓰지 않고, 보유 열쇠 표시(06 스펙 §4.3)를 붙일 때의 대조 키다.
 * 아이스브레이커(11 스펙)의 열쇠 없는 문: keypad(keyTpl·keyName 빈 문자열, 고정 코드면 code), explosive(SZ-1 폭약),
 * hatch(가스 토치) — keyName 은 필요한 아이템 이름.
 */
export interface LockedDoor {
  keyTpl: string
  keyName: string
  kind: 'door' | 'keycard' | 'keypad' | 'explosive' | 'hatch'
  position: MapPoint
  code?: string | null
}

/**
 * 탈출구·환승(10 스펙). name 은 tarkov.dev 영문 이름(모든 UI 언어 공통), 환승은 빈 문자열이고 target 이 목적지 맵 키.
 * requirement 는 서버가 로케일로 푼 조건 문구, 문구가 없는 조건은 requirementKind 코드로 와서 웹 i18n 이 번역한다.
 * chance 는 등장 확률이 100% 미만일 때만.
 */
export interface MapExit {
  key: string
  name: string
  kind: 'pmc' | 'shared' | 'scav' | 'transit'
  position: MapPoint
  requirement?: string | null
  requirementKind?: 'coop' | 'train' | 'secret' | 'alpinist' | 'switch' | null
  chance?: number | null
  target?: string | null
}

export interface Objective {
  conditionId: string
  conditionType: string
  /** 조건 로케일이 없으면 빈 문자열. 이때 targetName 으로 대체 문구를 만든다. */
  text: string
  /** text 가 빈 경우에만 채워지는 대상 아이템 이름. 아이템 조건이 아니거나 이름도 없으면 null. */
  targetName: string | null
  targetCount: number | null
  prep: ObjectivePrep | null
  /** 목표 위치. 좌표를 모르는 조건이면 빈 배열. 필드가 생기기 전 서버(구버전 DLL)는 undefined 를 보낸다. */
  locations?: ObjectiveLocation[]
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

/** 대상 퀘스트가 statuses 중 하나가 되면 이 퀘스트는 실패한다 (conditions.Fail 의 Quest 조건 = 배타 분기) */
export interface FailTrigger {
  questId: string
  statuses: string[]
}

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
  modName: string | null   // isVanilla=false 일 때만 값이 있을 수 있다. 서버가 출처 모드를 못 찾으면 null
  imageUrl: string | null      // 위키에서는 쓰지 않는다 (스펙 §0)
  minLevel: number | null
  /** 퀘스트가 묶인 맵의 표시 이름. 아무 맵이면 null */
  location: string | null
  /** 퀘스트가 묶인 맵 키(locations 폴더 이름, 소문자, 예: bigmap). 아무 맵이면 null. 구버전 서버엔 없다(undefined) */
  locationKey?: string | null
  requirements: Requirement[]
  prerequisites: string[]
  unlocks: string[]
  failsWhen: FailTrigger[]
  objectives: Objective[]
  rewards: QuestRewards
  tags: string[]               // "isolated" | "traderInternal"
}

export interface CatalogWarning {
  questId?: string
  code: string
  detail: string
}

/** 핸드북 최상위 카테고리. 이름은 i18n itemCat.<id> */
export interface CatalogItemCategory {
  id: string
  iconUrl: string | null
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
  /** 제출·설치 아이템이 속한 핸드북 최상위 카테고리, 표시 순서대로. 이름은 i18n itemCat.<id> */
  itemCategories: CatalogItemCategory[]
  /** 아이템 tpl → 카테고리 id. 핸드북에 없는 아이템은 빠진다 */
  itemCategoryOf: Record<string, string>
  /** 서버 맵 키 → 잠긴 문. 필드가 생기기 전 서버(구버전 DLL)는 보내지 않는다. */
  lockedDoors?: Record<string, LockedDoor[]>
  /** 서버 맵 키 → 활성 지도 변형 ID(맵 교체 모드가 로드된 서버만, 예: interchange → manimal). 구버전 서버는 보내지 않는다. */
  mapVariants?: Record<string, string>
  /** 서버 맵 키 → 탈출구·환승. 구버전 서버면 undefined. */
  exits?: Record<string, MapExit[]>
  /** 아이템 tpl → 주로 나오는 곳(Collector 제출 아이템만). 구버전 서버면 undefined. */
  lootSources?: Record<string, LootSource>
}

/** containers = 하나 열었을 때 들어 있을 확률 높은 순, bots = 봇 묶음 키 */
export interface LootSource {
  containers: LootContainer[]
  bots: BotGroup[]
}

export interface LootContainer {
  tpl: string
  name: string
  /** 그 컨테이너 하나를 열었을 때 아이템이 하나 이상 들어 있을 확률 0~1 */
  chance: number
}

export type BotGroup = 'scav' | 'pmc' | 'boss' | 'raider' | 'cultist' | 'other'

export interface CatalogItemCategory {
  id: string
  iconUrl: string | null
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
  return getJson<Catalog>(`/questcodex/api/catalog?lang=${encodeURIComponent(lang)}`, signal)
}

/** QuestCodex REST 공통 GET. 실패는 CatalogError(code) — 이름은 카탈로그지만 진행 상태 API 도 같은 에러 규약이다. */
export async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const res = await send(url, { signal })
  return (await res.json()) as T
}

/** 서버의 메모리·디스크 캐시를 버린다(13 catalog-cache 스펙 §4). 이어서 fetchCatalog 를 다시 부르면 새로 만든다. */
export async function rebuildCatalog(): Promise<void> {
  await send('/questcodex/api/catalog/rebuild', { method: 'POST' })
}

async function send(url: string, init: RequestInit): Promise<Response> {
  let res: Response
  try {
    res = await fetch(url, init)
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
  return res
}
