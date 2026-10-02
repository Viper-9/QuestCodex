import { describe, expect, it } from 'vitest'
import type { Catalog, CatalogQuest, Requirement, Reward } from '../api/catalog'
import type { ProfileProgress, QuestProgress } from '../api/progress'
import { filterUnlockRows, nextStep, sourcePath, unlockCounts, unlockRows, type UnlockSource } from './unlock'

function quest(id: string, p: Partial<CatalogQuest> = {}): CatalogQuest {
  return {
    id, name: id, description: '', traderId: 't1', side: 'Pmc', factionOnly: null, isVanilla: true, modName: null, imageUrl: null,
    minLevel: null, location: null, locationKey: null, requirements: [], prerequisites: [], unlocks: [], failsWhen: [], objectives: [],
    rewards: { started: [], success: [], fail: [] }, tags: [], ...p,
  }
}

const after = (questId: string, ...needStatuses: string[]): Requirement =>
  ({ kind: 'quest', questId, needStatuses, availableAfterSec: 0, resolved: true })
const sale = (tpl: string, name = tpl, loyaltyLevel = 1): Reward =>
  ({ kind: 'assortUnlock', traderId: 't1', tpl, name, iconUrl: null, loyaltyLevel, categories: [] })
const craft = (tpl: string, name = tpl): Reward => ({ kind: 'production', areaType: 10, tpl, name, iconUrl: null })

function catalog(quests: CatalogQuest[]): Catalog {
  return {
    sptVersion: '', modVersion: '', generatedAt: '', lang: 'en', traders: {}, rewardIndex: {}, warnings: [], itemCategories: [], itemCategoryOf: {},
    quests: Object.fromEntries(quests.map((q) => [q.id, q])),
  }
}

const qp = (status: string, lockReasons: QuestProgress['lockReasons'] = []): QuestProgress =>
  ({ status, startTime: null, finishTime: null, lockReasons, objectives: {} })
/** 잠겼지만 사유가 있는(= 원인을 아는) 상태 */
const locked = qp('Locked', [{ kind: 'level', need: 99, compare: '>=', current: 1 }])

function progress(quests: Record<string, QuestProgress>, side = 'Usec'): ProfileProgress {
  return { profileId: 'p', nickname: 'p', level: 1, side, isActive: true, quests, traderStats: {}, inventory: {}, warnings: [] }
}

const src = (questId: string, p: Partial<UnlockSource> = {}): UnlockSource =>
  ({ questId, kind: 'sale', phase: 'success', traderId: 't1', loyaltyLevel: 1, areaType: null, ...p })

describe('sourcePath', () => {
  it('1. 선행 사슬 A→B→목표, A 진행 중 → 3단계, 지금 할 것 A', () => {
    const cat = catalog([quest('A'), quest('B', { requirements: [after('A', 'Success')] }), quest('T', { requirements: [after('B', 'Success')] })])
    const plan = sourcePath(cat, progress({ A: qp('Started'), B: locked, T: locked }), src('T'))
    expect(plan.state).toBe('reachable')
    expect(plan.steps.map((s) => [s.questId, s.mode, s.depth])).toEqual([['A', 'complete', 0], ['B', 'complete', 1], ['T', 'complete', 2]])
    expect(plan.remaining).toBe(3)
    expect(nextStep(plan)?.questId).toBe('A')
  })

  it('2. 수락만 요구하는 선행은 start 단계', () => {
    const cat = catalog([quest('A'), quest('T', { requirements: [after('A', 'Started', 'Success')] })])
    const plan = sourcePath(cat, progress({ A: qp('AvailableForStart'), T: locked }), src('T'))
    expect(plan.steps.map((s) => [s.questId, s.mode])).toEqual([['A', 'start'], ['T', 'complete']])
  })

  it('3. 선행을 이미 실패 → failed / 실패해야 하는데 완료함 → branch', () => {
    const failed = catalog([quest('A'), quest('B', { requirements: [after('A', 'Success')] }), quest('T', { requirements: [after('B', 'Success')] })])
    expect(sourcePath(failed, progress({ A: qp('Started'), B: qp('Fail'), T: locked }), src('T')).blockReason)
      .toEqual({ kind: 'failed', questId: 'B' })

    const branch = catalog([quest('A'), quest('T', { requirements: [after('A', 'Fail')] })])
    const plan = sourcePath(branch, progress({ A: qp('Success'), T: locked }), src('T'))
    expect(plan.state).toBe('blocked')
    expect(plan.blockReason).toEqual({ kind: 'branch', questId: 'A' })
  })

  it('4. 다른 진영 전용 → faction', () => {
    const cat = catalog([quest('T', { factionOnly: 'bear' })])
    expect(sourcePath(cat, progress({ T: locked }, 'Usec'), src('T')).blockReason).toEqual({ kind: 'faction', questId: 'T' })
    expect(sourcePath(cat, progress({ T: locked }, 'Bear'), src('T')).state).toBe('reachable')
  })

  it('5. Locked 인데 사유가 비어 있으면 unknownLock', () => {
    const cat = catalog([quest('T')])
    expect(sourcePath(cat, progress({ T: qp('Locked') }), src('T')).blockReason).toEqual({ kind: 'unknownLock', questId: 'T' })
  })

  it('6. started 보상은 목표를 수락만 해도 해금', () => {
    const cat = catalog([quest('T')])
    expect(sourcePath(cat, progress({ T: qp('Started') }), src('T', { phase: 'started' })).state).toBe('unlocked')
    expect(sourcePath(cat, progress({ T: qp('Started') }), src('T')).state).toBe('reachable')
    const open = sourcePath(cat, progress({ T: qp('AvailableForStart') }), src('T', { phase: 'started' }))
    expect(open.steps.map((s) => s.mode)).toEqual(['start'])
  })

  it('9. 두 갈래가 합류하면 depth 는 가장 긴 사슬', () => {
    const cat = catalog([
      quest('R1'), quest('R2'), quest('A', { requirements: [after('R1', 'Success')] }),
      quest('M', { requirements: [after('A', 'Success'), after('R2', 'Success')] }),
      quest('T', { requirements: [after('M', 'Success')] }),
    ])
    const plan = sourcePath(cat, progress({ R1: qp('AvailableForStart'), R2: qp('AvailableForStart'), A: locked, M: locked, T: locked }), src('T'))
    expect(plan.steps.map((s) => [s.questId, s.depth])).toEqual([['R1', 0], ['R2', 0], ['A', 1], ['M', 2], ['T', 3]])
  })

  it('경로의 레벨·상인 요구는 최댓값으로 모은다', () => {
    const cat = catalog([
      quest('A', { requirements: [{ kind: 'level', value: 20, compare: '>=' }, { kind: 'traderLoyalty', traderId: 't2', value: 2, compare: '>=' }] }),
      quest('T', { requirements: [after('A', 'Success'), { kind: 'level', value: 15, compare: '>=' }, { kind: 'traderLoyalty', traderId: 't2', value: 3, compare: '>=' }] }),
    ])
    const plan = sourcePath(cat, progress({ A: qp('AvailableForStart'), T: locked }), src('T'))
    expect(plan.maxLevel).toBe(20)
    expect(plan.traderReqs).toEqual([{ traderId: 't2', kind: 'loyalty', value: 3 }])
  })

  it('레벨 1 요구는 제한 없음(null)으로 본다', () => {
    const cat = catalog([quest('T', { requirements: [{ kind: 'level', value: 1, compare: '>=' }] })])
    expect(sourcePath(cat, progress({ T: qp('AvailableForStart') }), src('T')).maxLevel).toBeNull()
  })
})

describe('unlockRows', () => {
  it('7. 판매 미해금 + 제작 해금 → remaining + craftUnlocked, 이미 해금 칩에 안 셈', () => {
    const cat = catalog([
      quest('C', { rewards: { started: [], success: [craft('ammo', ' M855A1 ')], fail: [] } }),
      quest('S', { rewards: { started: [], success: [sale('ammo', 'M855A1', 3)], fail: [] } }),
    ])
    const [row] = unlockRows(cat, progress({ C: qp('Success'), S: qp('AvailableForStart') }))
    expect(row).toMatchObject({ tpl: 'ammo', name: 'M855A1', category: 'other', state: 'remaining', craftUnlocked: true })
    expect(row.best?.source.questId).toBe('S')
    expect(row.plans.map((p) => p.state)).toEqual(['reachable', 'unlocked'])
    expect(unlockCounts([row])).toEqual({ all: 1, sale: 1, craft: 0, unlocked: 0, unreachable: 0 })
  })

  it('8. 남은 퀘스트 수 → 이름 순, 도달 불가는 칩으로만', () => {
    const cat = catalog([
      quest('A'), quest('B', { requirements: [after('A', 'Success')] }),
      quest('Q1', { rewards: { started: [], success: [sale('x', 'Zeta')], fail: [] } }),
      quest('Q2', { requirements: [after('A', 'Success')], rewards: { started: [], success: [sale('y', 'Alpha')], fail: [] } }),
      quest('Q3', { rewards: { started: [], success: [sale('z', 'Beta')], fail: [] } }),
      quest('Q4', { factionOnly: 'bear', rewards: { started: [], success: [craft('w', 'Omega')], fail: [] } }),
    ])
    const rows = unlockRows(cat, progress({ A: qp('Started'), B: locked, Q1: qp('AvailableForStart'), Q2: locked, Q3: qp('AvailableForStart'), Q4: locked }))
    expect(filterUnlockRows(rows, 'all', '').map((r) => r.name)).toEqual(['Beta', 'Zeta', 'Alpha'])
    expect(filterUnlockRows(rows, 'unreachable', '').map((r) => r.name)).toEqual(['Omega'])
    expect(filterUnlockRows(rows, 'all', 'z e').map((r) => r.name)).toEqual(['Zeta'])
    expect(unlockCounts(rows)).toEqual({ all: 3, sale: 3, craft: 0, unlocked: 0, unreachable: 1 })
  })
})
