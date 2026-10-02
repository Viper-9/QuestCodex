import type { LockedDoor, Objective, Requirement, Reward } from '../api/catalog'
import type { T, UiKey } from '../i18n/index'
import type { NameLookup } from './derive'

// UI 에 의존하지 않는 표시 문자열 계산 (스펙 §4.1, §4.2). JSX 를 만들지 않고
// "조각(parts)" 을 돌려줘서 LineList 가 questId 가 있는 조각만 링크로 그린다.
// 문구는 전부 t('fmt.*') — i18n/<lang>.json 에서 온다. t 를 인자로 받으므로 React 없이 테스트된다.

export type Tone = 'normal' | 'muted' | 'warn'

export interface TextPart {
  text: string
  /** 있으면 이 조각은 해당 퀘스트로 점프하는 링크 */
  questId?: string
}

export interface FormattedLine {
  parts: TextPart[]
  tone: Tone
}

const plain = (text: string, tone: Tone = 'normal'): FormattedLine => ({ parts: [{ text }], tone })

/** 테스트·툴팁용: 조각을 이어붙인 전체 문장 */
export function lineText(line: FormattedLine): string {
  return line.parts.map((p) => p.text).join('')
}

export function formatInt(n: number): string {
  return n.toLocaleString('en-US')
}

export function formatSigned(n: number): string {
  return n > 0 ? `+${n}` : `${n}`
}

/** 초 → H:MM (시는 자리수 제한 없음) */
export function formatDuration(sec: number): string {
  const h = Math.floor(sec / 3600)
  const m = Math.floor((sec % 3600) / 60)
  return `${h}:${String(m).padStart(2, '0')}`
}

export function shortId(id: string): string {
  return id.slice(0, 8)
}

/** needStatuses → 완료 / 시작 / 원문 나열 (§4.1 quest 행) */
function statusLabel(needStatuses: string[], t: T): string {
  if (needStatuses.includes('Success')) return t('fmt.questDone')
  if (needStatuses.length === 1 && needStatuses[0] === 'Started') return t('fmt.questStarted')
  return needStatuses.join('/')
}

export function formatRequirement(r: Requirement, lookup: NameLookup, t: T): FormattedLine {
  switch (r.kind) {
    case 'level':
      return plain(t('fmt.level', { compare: r.compare, value: r.value }))
    case 'quest': {
      if (!r.resolved) return plain(t('fmt.unknownQuest', { id: shortId(r.questId) }), 'warn')
      const parts: TextPart[] = [
        { text: lookup.questName(r.questId) ?? r.questId, questId: r.questId },
        { text: ` ${statusLabel(r.needStatuses, t)}` },
      ]
      if (r.availableAfterSec > 0) parts.push({ text: t('fmt.questWait', { duration: formatDuration(r.availableAfterSec) }) })
      return { parts, tone: 'normal' }
    }
    case 'traderLoyalty':
      return plain(t('fmt.loyalty', { trader: lookup.traderName(r.traderId), compare: r.compare, value: r.value }))
    case 'traderStanding':
      return plain(t('fmt.standing', { trader: lookup.traderName(r.traderId), compare: r.compare, value: r.value }))
    case 'other':
      return r.text !== '' ? plain(r.text) : plain(otherConditionText(r.conditionType, r.targetName, t), 'muted')
  }
}

/** 문구를 따로 둔 시작 조건 타입. 여기 없는 타입은 objectiveFallback("타입: 아이템") 으로 */
const OTHER_CONDITION_KEYS: Partial<Record<string, UiKey>> = {
  FindItem: 'fmt.findItemToStart',   // Icebreaker "Boreas": 전단지를 주우면 시작
}

/**
 * 해석하지 않는 시작 조건에 로케일이 없을 때의 문구. 목표의 대체 문구(formatObjective)와 같은 규칙 —
 * 서버가 대상 아이템 이름만 주고 문장 조립은 여기서 한다. 잠김 사유 요약(progress/format)도 이걸 쓴다.
 */
export function otherConditionText(conditionType: string, targetName: string | null, t: T): string {
  if (targetName === null) return conditionType
  const key = OTHER_CONDITION_KEYS[conditionType]
  return key ? t(key, { name: targetName }) : t('fmt.objectiveFallback', { type: conditionType, name: targetName })
}

export function formatReward(r: Reward, lookup: NameLookup, t: T): FormattedLine {
  switch (r.kind) {
    case 'item':
      return plain(t('fmt.item', { name: r.name, count: formatInt(r.count) }))
    case 'assortUnlock':
      return plain(t('fmt.assortUnlock', { trader: lookup.traderName(r.traderId), name: r.name ?? t('fmt.unknownItem'), level: r.loyaltyLevel }))
    case 'production':
      return plain(t('fmt.production', { name: r.name ?? t('fmt.unknownProduction') }))
    case 'traderUnlock':
      return plain(t('fmt.traderUnlock', { trader: lookup.traderName(r.traderId) }))
    case 'traderStanding':
      return plain(t('fmt.standingReward', { trader: lookup.traderName(r.traderId), value: formatSigned(r.value) }))
    case 'experience':
      return plain(t('fmt.experience', { value: formatInt(r.value) }))
    case 'skill':
      return plain(t('fmt.skill', { skill: r.skill, value: r.value }))
    case 'achievement':
      return plain(t('fmt.achievement', { id: r.achievementId }))
    case 'other':
      return plain(r.rewardType, 'muted')
  }
}

export function formatObjective(o: Objective, t: T): FormattedLine {
  // text 가 비면 서버가 조건 로케일을 못 찾은 것. 모드가 조건만 추가하고 번역을 빼먹으면 여기로 온다.
  // 이때는 conditionType 에 대상 아이템 이름을 붙여 대체 문구를 만든다.
  let text = o.text
  if (text === '') {
    text = o.targetName === null
      ? o.conditionType
      : t('fmt.objectiveFallback', { type: o.conditionType, name: o.targetName })
  }
  if (o.targetCount !== null) text += t('fmt.objectiveCount', { n: formatInt(o.targetCount) })
  return plain(text)
}

/** 잠긴 문 말풍선(06·11 스펙): 열쇠는 이름만, 키카드·폭파문·해치는 종류 + 필요한 아이템, 키패드는 고정 코드가 있으면 코드. */
export function doorLabel(door: LockedDoor, t: T): string {
  switch (door.kind) {
    case 'keycard': return `${t('map.keycard')} · ${door.keyName}`
    case 'keypad': return door.code ? `${t('map.keypad')} · ${t('map.keypadCode', { code: door.code })}` : t('map.keypad')
    case 'explosive': return t('map.explosiveDoor', { item: door.keyName })
    case 'hatch': return t('map.frozenHatch', { item: door.keyName })
    default: return door.keyName
  }
}
