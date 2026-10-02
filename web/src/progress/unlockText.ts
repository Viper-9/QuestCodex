import type { Catalog } from '../api/catalog'
import type { T, UiKey } from '../i18n/index'
import type { NameLookup } from '../wiki/derive'
import { isClosed } from './derive'
import { statusLabel } from './format'
import type { PathStep, SourcePlan, UnlockSource } from './unlock'

// 해금 경로 표시 문자열. format.ts 와 같은 규칙 — t 를 인자로 받아 React 없이 테스트된다.

/** 실데이터에 나오는 은신처 시설(화장실·의무실·작업대·정보 센터)만 이름, 나머지는 번호 */
const AREAS = new Set([2, 7, 10, 11])

export function areaName(areaType: number | null, t: T): string {
  return areaType !== null && AREAS.has(areaType) ? t(`hideout.area.${areaType}` as UiKey) : t('hideout.area.unknown', { n: areaType ?? '?' })
}

/** "프라퍼 LL4 판매" / "제작 · 작업대" */
export function sourceLabel(s: UnlockSource, lookup: NameLookup, t: T): string {
  return s.kind === 'sale'
    ? t('unlock.sale', { trader: lookup.traderName(s.traderId ?? ''), n: s.loyaltyLevel ?? 1 })
    : t('unlock.craft', { area: areaName(s.areaType, t) })
}

/** 카탈로그에 없는 퀘스트(요구 조건이 가리키는 삭제된 퀘스트 등)는 "알 수 없는 퀘스트" */
export function questNameOf(catalog: Catalog, id: string, t: T): string {
  return catalog.quests[id]?.name ?? t('unlock.unknownQuest')
}

export function blockText(r: NonNullable<SourcePlan['blockReason']>, catalog: Catalog, t: T): string {
  return t(`unlock.block.${r.kind}` as UiKey, { quest: questNameOf(catalog, r.questId, t) })
}

/** 경로 단계 오른쪽 문구. 지금 할 것 > 이미 끝난 상태(실패함 등) > 실패시켜야 열림 > 수락만 하면 됨 > 목표 > 현재 상태 */
export function stepNote(step: PathStep, at: { now: boolean; goal: boolean }, t: T): string {
  if (at.now) return `${statusLabel(step.status, t)} — ${t('unlock.step.now')}`
  if (isClosed(step.status)) return statusLabel(step.status, t)
  if (step.mode === 'fail') return t('unlock.step.fail')
  if (step.mode === 'start') return t('unlock.step.start')
  if (at.goal) return t('unlock.step.goal')
  return statusLabel(step.status, t)
}
