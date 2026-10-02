// 프로필 진행 상태 API (dev-docs/07-progress/api-reference.md §2·§3) 의 TS 판.
import { getJson } from './catalog'

export interface ProfileSummary {
  id: string
  nickname: string
  level: number | null
  side: string | null
  hasCharacter: boolean
  /** 최근 30분 안에 접속 */
  isActive: boolean
  lastSessionAt: string | null
}

/** Locked · AvailableForStart · Started · AvailableForFinish · Success · Fail … (프로필에 없는 퀘스트는 Locked) */
export type QuestStatus = string

export type LockReason =
  | { kind: 'quest'; questId: string; needStatuses: string[]; currentStatus: QuestStatus }
  | { kind: 'level'; need: number; compare: string; current: number }
  | { kind: 'traderLoyalty'; traderId: string; need: number; compare: string; current: number }
  | { kind: 'traderStanding'; traderId: string; need: number; compare: string; current: number }
  | { kind: 'faction'; need: string }
  | { kind: 'other'; conditionType: string; text: string; targetName: string | null }

/** 목표 카운터 — 제출한 수·처치 수 등. 퀘스트의 모든 목표에 대해 온다. */
export interface ObjectiveProgress {
  current: number
  target: number | null
  done: boolean
}

export interface QuestProgress {
  status: QuestStatus
  startTime: string | null
  finishTime: string | null
  /** Locked 일 때만 채워진다 */
  lockReasons: LockReason[]
  objectives: Record<string, ObjectiveProgress>
}

export interface TraderStats {
  total: number
  success: number
  started: number
  availableForStart: number
  locked: number
  other: number
}

/** 퀘스트 아이템 보유 수(창고 + 입은 장비). fir = 그중 레이드 획득 */
export interface Holding {
  count: number
  fir: number
}

export interface ProfileProgress {
  profileId: string
  nickname: string
  level: number
  side: string
  isActive: boolean
  quests: Record<string, QuestProgress>
  traderStats: Record<string, TraderStats>
  /** 보유 0 은 생략. 보유 수 기능 이전 서버엔 필드 자체가 없다(undefined) */
  inventory?: Record<string, Holding>
  warnings: unknown[]
}

export function fetchProfiles(signal?: AbortSignal): Promise<ProfileSummary[]> {
  return getJson<ProfileSummary[]>('/questcodex/api/profiles', signal)
}

export function fetchProgress(profileId: string, signal?: AbortSignal): Promise<ProfileProgress> {
  return getJson<ProfileProgress>(`/questcodex/api/profiles/${encodeURIComponent(profileId)}/progress`, signal)
}
