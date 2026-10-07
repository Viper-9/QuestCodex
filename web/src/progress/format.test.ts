import { describe, expect, it } from 'vitest'
import { getT } from '../i18n/index'
import type { NameLookup } from '../wiki/derive'
import type { CatalogQuest } from '../api/catalog'
import type { QuestProgress } from '../api/progress'
import { lockReasonText, requirementLines, statusLabel } from './format'
import { objectiveLines } from './parts'

const ko = getT('kr')
const en = getT('en')
const lookup: NameLookup = { traderName: (id) => (id === 'pk' ? 'Peacekeeper' : id), questName: (id) => (id === 'q1' ? 'Mentor' : undefined) }

describe('statusLabel', () => {
  it('알려진 상태는 번역, 모르는 상태는 원문', () => {
    expect(statusLabel('AvailableForFinish', ko)).toBe('완료 보고 대기')
    expect(statusLabel('Started', en)).toBe('Active')
    expect(statusLabel('Weird', en)).toBe('Weird')
  })
})

describe('lockReasonText', () => {
  it('종류별 문구, 평판·충성도만 현재 값을 함께', () => {
    expect(lockReasonText({ kind: 'quest', questId: 'q1', needStatuses: ['Success'], currentStatus: 'Started' }, lookup, en))
      .toBe('Mentor: Completed needed')
    expect(lockReasonText({ kind: 'level', need: 61, compare: '>=', current: 47 }, lookup, ko)).toBe('레벨 61 필요')
    expect(lockReasonText({ kind: 'traderStanding', traderId: 'pk', need: 0.4, compare: '>=', current: 0.123 }, lookup, en))
      .toBe('Peacekeeper standing 0.40 needed (now 0.12)')
    expect(lockReasonText({ kind: 'faction', need: 'bear' }, lookup, en)).toBe('BEAR only')
  })
  it('카탈로그에 없는 선행은 ID 그대로', () => {
    expect(lockReasonText({ kind: 'quest', questId: 'zz', needStatuses: ['Fail'], currentStatus: 'Success' }, lookup, ko))
      .toBe('zz: 실패 필요')
  })
})

function quest(p: Partial<CatalogQuest> = {}): CatalogQuest {
  return {
    id: 'x', name: 'x', description: '', traderId: 'pk', side: 'Pmc', factionOnly: null, isVanilla: true, modName: null, imageUrl: null,
    minLevel: null, location: null, locationKey: null, requirements: [], prerequisites: [], unlocks: [], failsWhen: [], objectives: [],
    rewards: { started: [], success: [], fail: [] }, tags: [], ...p,
  }
}

const qp = (status: string, p: Partial<QuestProgress> = {}): QuestProgress =>
  ({ status, startTime: null, finishTime: null, lockReasons: [], objectives: {}, ...p })

describe('objectiveLines', () => {
  it('끝난 목표에 완료 표시', () => {
    const q = quest({ objectives: [
      { conditionId: 'a', conditionType: 'CounterCreator', text: 'Kill', targetName: null, targetCount: 3, prep: null },
      { conditionId: 'b', conditionType: 'HandoverItem', text: 'Hand', targetName: null, targetCount: 1, prep: null },
    ] })
    const lines = objectiveLines(q, qp('Started', { objectives: { a: { current: 1, target: 3, done: false }, b: { current: 1, target: 1, done: true } } }), en)
    expect(lines.map((l) => l.state)).toEqual([undefined, 'done'])
  })
})

describe('requirementLines', () => {
  const q = quest({ requirements: [
    { kind: 'level', value: 20, compare: '>=' },
    { kind: 'quest', questId: 'q1', needStatuses: ['Success'], availableAfterSec: 0, resolved: true },
    { kind: 'traderLoyalty', traderId: 'pk', value: 3, compare: '>=' },
  ] })

  it('잠기지 않은 퀘스트는 위키 문구 그대로', () => {
    expect(requirementLines(q, qp('Started'), lookup, en).every((l) => l.state === undefined)).toBe(true)
  })

  it('잠김 사유와 짝지은 조건만 미충족 + 현재 값, 짝 없는 사유는 끝에', () => {
    const lines = requirementLines(q, qp('Locked', { lockReasons: [
      { kind: 'quest', questId: 'q1', needStatuses: ['Success'], currentStatus: 'Started' },
      { kind: 'traderLoyalty', traderId: 'pk', need: 3, compare: '>=', current: 2 },
      { kind: 'faction', need: 'bear' },
    ] }), lookup, ko)
    expect(lines.map((l) => [l.state, l.aside ?? null])).toEqual([
      [undefined, null],
      ['unmet', null],
      ['unmet', '지금 2'],
      ['unmet', null],
    ])
    expect(lines[3].parts[0].text).toBe('BEAR 전용')
  })
})
