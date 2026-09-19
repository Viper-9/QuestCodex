import { describe, expect, it } from 'vitest'
import { getT } from '../i18n/index'
import type { NameLookup } from './derive'
import { formatDuration, formatInt, formatObjective, formatRequirement, formatReward, formatSigned, lineText, shortId } from './format'

const ko = getT('kr')
const en = getT('en')
const lookup: NameLookup = {
  traderName: (id) => ({ prapor: 'Prapor', skier: 'Skier' } as Record<string, string>)[id] ?? id,
  questName: (id) => ({ debut: 'Debut', checking: 'Checking' } as Record<string, string>)[id],
}

describe('formatRequirement (§4.1)', () => {
  it('level: 레벨 {compare} {value}', () => {
    expect(lineText(formatRequirement({ kind: 'level', value: 2, compare: '>=' }, lookup, ko))).toBe('레벨 >= 2')
    expect(lineText(formatRequirement({ kind: 'level', value: 2, compare: '>=' }, lookup, en))).toBe('Level >= 2')
  })
  it('quest Success 포함 → 이름 링크 + 완료', () => {
    const l = formatRequirement({ kind: 'quest', questId: 'debut', needStatuses: ['Success'], availableAfterSec: 0, resolved: true }, lookup, ko)
    expect(l.parts).toEqual([{ text: 'Debut', questId: 'debut' }, { text: ' 완료' }])
    expect(l.tone).toBe('normal')
  })
  it('quest Started 만 → 시작', () => {
    expect(lineText(formatRequirement({ kind: 'quest', questId: 'debut', needStatuses: ['Started'], availableAfterSec: 0, resolved: true }, lookup, ko))).toBe('Debut 시작')
  })
  it('quest 그 외 상태는 원문 나열', () => {
    expect(lineText(formatRequirement({ kind: 'quest', questId: 'debut', needStatuses: ['AvailableForFinish', 'Fail'], availableAfterSec: 0, resolved: true }, lookup, ko))).toBe('Debut AvailableForFinish/Fail')
  })
  it('quest availableAfterSec > 0 → (완료 후 H:MM 대기)', () => {
    expect(lineText(formatRequirement({ kind: 'quest', questId: 'debut', needStatuses: ['Success'], availableAfterSec: 5400, resolved: true }, lookup, ko))).toBe('Debut 완료 (완료 후 1:30 대기)')
  })
  it('quest resolved:false → 알 수 없는 퀘스트 (id 앞 8자), warn, 링크 없음', () => {
    const l = formatRequirement({ kind: 'quest', questId: '5f2a91c0abcdef0123456789', needStatuses: ['Success'], availableAfterSec: 0, resolved: false }, lookup, ko)
    expect(lineText(l)).toBe('알 수 없는 퀘스트 (5f2a91c0)')
    expect(l.tone).toBe('warn')
    expect(l.parts[0].questId).toBeUndefined()
  })
  it('quest resolved 인데 이름 조회 실패면 id 를 라벨로', () => {
    expect(lineText(formatRequirement({ kind: 'quest', questId: 'ghost', needStatuses: ['Success'], availableAfterSec: 0, resolved: true }, lookup, ko))).toBe('ghost 완료')
  })
  it('traderLoyalty / traderStanding', () => {
    expect(lineText(formatRequirement({ kind: 'traderLoyalty', traderId: 'prapor', value: 2, compare: '>=' }, lookup, ko))).toBe('Prapor 충성도 >= 2')
    expect(lineText(formatRequirement({ kind: 'traderStanding', traderId: 'skier', value: 0.5, compare: '>' }, lookup, ko))).toBe('Skier 평판 > 0.5')
  })
  it('other: text 있으면 text, 없으면 conditionType 회색', () => {
    expect(formatRequirement({ kind: 'other', conditionType: 'Skill', text: '근력 10' }, lookup, ko)).toEqual({ parts: [{ text: '근력 10' }], tone: 'normal' })
    expect(formatRequirement({ kind: 'other', conditionType: 'Skill', text: '' }, lookup, ko)).toEqual({ parts: [{ text: 'Skill' }], tone: 'muted' })
  })
})

describe('formatReward (§4.2)', () => {
  it('item: {name} ×{count}', () => {
    expect(lineText(formatReward({ kind: 'item', tpl: 't', name: '루블', iconUrl: null, count: 15000, categories: [] }, lookup, ko))).toBe('루블 ×15,000')
  })
  it('assortUnlock', () => {
    expect(lineText(formatReward({ kind: 'assortUnlock', traderId: 'prapor', tpl: 't', name: 'PM 9x18', iconUrl: null, loyaltyLevel: 1, categories: [] }, lookup, ko))).toBe('Prapor 판매 해금: PM 9x18 (LL1)')
    expect(lineText(formatReward({ kind: 'assortUnlock', traderId: 'prapor', tpl: null, name: null, iconUrl: null, loyaltyLevel: 2, categories: [] }, lookup, ko))).toBe('Prapor 판매 해금: 알 수 없는 아이템 (LL2)')
  })
  it('production', () => {
    expect(lineText(formatReward({ kind: 'production', areaType: 10, tpl: 't', name: 'Salewa', iconUrl: null }, lookup, ko))).toBe('제작 해금: Salewa')
    expect(lineText(formatReward({ kind: 'production', areaType: 10, tpl: null, name: null, iconUrl: null }, lookup, ko))).toBe('제작 해금: 알 수 없는 제작')
  })
  it('traderUnlock / traderStanding / experience / skill / achievement', () => {
    expect(lineText(formatReward({ kind: 'traderUnlock', traderId: 'skier' }, lookup, ko))).toBe('Skier 해금')
    expect(lineText(formatReward({ kind: 'traderStanding', traderId: 'prapor', value: 0.02 }, lookup, ko))).toBe('Prapor 평판 +0.02')
    expect(lineText(formatReward({ kind: 'traderStanding', traderId: 'prapor', value: -0.05 }, lookup, ko))).toBe('Prapor 평판 -0.05')
    expect(lineText(formatReward({ kind: 'experience', value: 1800 }, lookup, ko))).toBe('경험치 1,800')
    expect(lineText(formatReward({ kind: 'experience', value: 1800 }, lookup, en))).toBe('Experience 1,800')
    expect(lineText(formatReward({ kind: 'skill', skill: 'Endurance', value: 100 }, lookup, ko))).toBe('Endurance +100')
    expect(lineText(formatReward({ kind: 'achievement', achievementId: '6512…' }, lookup, ko))).toBe('업적: 6512…')
  })
  it('other: rewardType 회색', () => {
    expect(formatReward({ kind: 'other', rewardType: 'StashRows' }, lookup, ko)).toEqual({ parts: [{ text: 'StashRows' }], tone: 'muted' })
  })
})

describe('formatObjective', () => {
  it('text + ×N + (선택)', () => {
    expect(lineText(formatObjective({ conditionId: 'c', conditionType: 'HandoverItem', text: '창고 열쇠 인도', targetCount: 1, optional: false }, ko))).toBe('창고 열쇠 인도 ×1')
    expect(lineText(formatObjective({ conditionId: 'c', conditionType: 'Kill', text: '스캐브 처치', targetCount: 25, optional: true }, ko))).toBe('스캐브 처치 ×25 (선택)')
    expect(lineText(formatObjective({ conditionId: 'c', conditionType: 'Visit', text: '창고 위치 확인', targetCount: null, optional: false }, ko))).toBe('창고 위치 확인')
  })
  it('text 비면 conditionType 회색', () => {
    expect(formatObjective({ conditionId: 'c', conditionType: 'CounterCreator', text: '', targetCount: null, optional: false }, ko)).toEqual({ parts: [{ text: 'CounterCreator' }], tone: 'muted' })
  })
})

describe('helpers', () => {
  it('formatInt 천 단위 콤마, 소수는 그대로', () => {
    expect(formatInt(1800)).toBe('1,800')
    expect(formatInt(15000)).toBe('15,000')
    expect(formatInt(7)).toBe('7')
    expect(formatInt(1.5)).toBe('1.5')
  })
  it('formatSigned', () => {
    expect(formatSigned(0.02)).toBe('+0.02')
    expect(formatSigned(-0.05)).toBe('-0.05')
    expect(formatSigned(0)).toBe('0')
  })
  it('formatDuration H:MM', () => {
    expect(formatDuration(5400)).toBe('1:30')
    expect(formatDuration(60)).toBe('0:01')
    expect(formatDuration(90000)).toBe('25:00')
  })
  it('shortId 앞 8자', () => {
    expect(shortId('5f2a91c0abcdef0123456789')).toBe('5f2a91c0')
    expect(shortId('abc')).toBe('abc')
  })
})
