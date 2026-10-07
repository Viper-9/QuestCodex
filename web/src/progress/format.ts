import type { CatalogQuest, Requirement } from '../api/catalog'
import type { LockReason, QuestProgress, QuestStatus } from '../api/progress'
import type { T, UiKey } from '../i18n/index'
import type { NameLookup } from '../wiki/derive'
import { formatInt, formatRequirement, otherConditionText } from '../wiki/format'
import type { ListLine } from '../wiki/LineList'

// 진행현황 표시 문자열. wiki/format.ts 와 같은 규칙 — t 를 인자로 받아 React 없이 테스트된다.

const STATUSES = new Set([
  'Locked', 'AvailableForStart', 'Started', 'AvailableForFinish', 'Success', 'Fail', 'FailRestartable', 'MarkedAsFailed', 'Expired',
])

/** 모르는 상태(모드·신규 SPT)는 원문 그대로 */
export function statusLabel(status: QuestStatus, t: T): string {
  return STATUSES.has(status) ? t(`status.${status}` as UiKey) : status
}

/** 핸드북 최상위 카테고리 id — 게임 로케일에 번역이 없어(kr 도 영어) i18n itemCat.<id> 로 이름을 붙인다 */
const ITEM_CATEGORIES = new Set([
  '5b5f78dc86f77409407a7f8e', '5b5f71a686f77447ed5636ab', '5b47574386f77428ca22b346', '5b47574386f77428ca22b33f',
  '6564b96a189fe36f356d177c', '5b47574386f77428ca22b344', '5b47574386f77428ca22b340', '5b47574386f77428ca22b33e',
  '5b47574386f77428ca22b341', '5b47574386f77428ca22b342', '5b47574386f77428ca22b343', '5b47574386f77428ca22b345',
  '5b619f1a86f77450a702a6f3', '5b5f78b786f77447ed5636af',
])

/** 모르는 카테고리(모드)와 핸드북에 없는 아이템은 "기타" */
export function itemCategoryLabel(id: string, t: T): string {
  return ITEM_CATEGORIES.has(id) ? t(`itemCat.${id}` as UiKey) : t('itemCat.other')
}

/** 평판은 소수 둘째 자리까지(서버가 2.11 같은 값을 준다), 나머지는 정수 */
function num(n: number): string {
  return Number.isInteger(n) ? formatInt(n) : n.toFixed(2)
}

export function lockReasonText(r: LockReason, lookup: NameLookup, t: T): string {
  switch (r.kind) {
    case 'quest':
      return t('lock.quest', {
        quest: lookup.questName(r.questId) ?? r.questId,
        need: r.needStatuses.map((s) => statusLabel(s, t)).join('/'),
      })
    case 'level':
      return t('lock.level', { need: r.need })
    case 'traderLoyalty':
      return t('lock.loyalty', { trader: lookup.traderName(r.traderId), need: r.need, now: r.current })
    case 'traderStanding':
      return t('lock.standing', { trader: lookup.traderName(r.traderId), need: num(r.need), now: num(r.current) })
    case 'faction':
      return t('lock.faction', { side: r.need.toUpperCase() })
    case 'other':
      return r.text !== '' ? r.text : otherConditionText(r.conditionType, r.targetName, t)
  }
}

/** 잠김 사유가 가리키는 시작 조건인가. faction 은 시작 조건이 아니라 퀘스트 속성이라 짝이 없다. */
function sameCondition(req: Requirement, r: LockReason): boolean {
  switch (r.kind) {
    case 'quest': return req.kind === 'quest' && req.questId === r.questId
    case 'level': return req.kind === 'level'
    case 'traderLoyalty': return req.kind === 'traderLoyalty' && req.traderId === r.traderId
    case 'traderStanding': return req.kind === 'traderStanding' && req.traderId === r.traderId
    case 'other': return req.kind === 'other' && req.conditionType === r.conditionType
    case 'faction': return false
  }
}

/** 잠김 사유의 현재 값. 미충족 줄 오른쪽에 "지금 47" 로 붙는다. 선행 퀘스트는 붙이지 않는다. */
function reasonNow(r: LockReason): string | null {
  switch (r.kind) {
    case 'quest': return null // 선행 퀘스트 상태는 줄 내용과 헷갈려 숨김
    case 'level':
    case 'traderLoyalty': return formatInt(r.current)
    case 'traderStanding': return num(r.current)
    case 'faction':
    case 'other': return null
  }
}

/**
 * 펼친 행의 시작 조건 줄. 잠긴 퀘스트만 서버의 잠김 사유와 짝지어 미충족(✗ + 현재 값)을 표시한다.
 * 서버는 못 채운 조건만 주므로 짝이 없는 조건은 충족으로 본다. 짝이 없는 사유(진영 등)는 끝에 덧붙인다.
 */
export function requirementLines(quest: CatalogQuest, qp: QuestProgress, lookup: NameLookup, t: T): ListLine[] {
  const lines: ListLine[] = quest.requirements.map((r) => formatRequirement(r, lookup, t))
  if (qp.status !== 'Locked') return lines
  const taken = new Set<number>()
  for (const reason of qp.lockReasons) {
    const i = quest.requirements.findIndex((req, k) => !taken.has(k) && sameCondition(req, reason))
    if (i < 0) {
      lines.push({ parts: [{ text: lockReasonText(reason, lookup, t) }], tone: 'normal', state: 'unmet' })
      continue
    }
    taken.add(i)
    const now = reasonNow(reason)
    lines[i] = { ...lines[i], state: 'unmet', aside: now === null ? null : t('overview.now', { now }) }
  }
  return lines
}
