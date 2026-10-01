import { describe, expect, it } from 'vitest'
import type { Catalog } from '../api/catalog'
import { getT } from '../i18n/index'
import type { NameLookup } from '../wiki/derive'
import type { PathStep, UnlockSource } from './unlock'
import { areaName, blockText, sourceLabel, stepNote } from './unlockText'

const ko = getT('kr')
const en = getT('en')
const lookup: NameLookup = { traderName: (id) => (id === 'pk' ? 'Peacekeeper' : id), questName: () => undefined }
const catalog = { quests: { q1: { name: 'Mentor' } } } as unknown as Catalog
const src = (p: Partial<UnlockSource>): UnlockSource =>
  ({ questId: 'q1', kind: 'sale', phase: 'success', traderId: 'pk', loyaltyLevel: 4, areaType: null, ...p })
const step = (p: Partial<PathStep>): PathStep => ({ questId: 'q1', mode: 'complete', depth: 0, status: 'Locked', failsOnComplete: [], ...p })

describe('unlockText', () => {
  it('은신처 시설은 아는 값만 이름, 나머지는 번호', () => {
    expect(areaName(10, ko)).toBe('작업대')
    expect(areaName(99, en)).toBe('Area 99')
  })

  it('입수처 이름 — 판매는 상인 LL, 제작은 시설', () => {
    expect(sourceLabel(src({}), lookup, ko)).toBe('Peacekeeper LL4 판매')
    expect(sourceLabel(src({ kind: 'craft', traderId: null, loyaltyLevel: null, areaType: 7 }), lookup, en)).toBe('Craft · Medstation')
  })

  it('막힌 사유 — 카탈로그에 없는 퀘스트는 "알 수 없는 퀘스트"', () => {
    expect(blockText({ kind: 'failed', questId: 'q1' }, catalog, en)).toBe('A quest on the path has failed: Mentor')
    expect(blockText({ kind: 'unknownLock', questId: 'zzz' }, catalog, ko)).toBe('열 수 없는 퀘스트(이벤트 등)를 거침: 알 수 없는 퀘스트')
  })

  it('단계 오른쪽 문구 — 지금 할 것 > 실패 > 수락만 > 목표 > 상태', () => {
    expect(stepNote(step({ status: 'Started' }), { now: true, goal: false }, ko)).toBe('진행 중 — 지금 할 것')
    expect(stepNote(step({ mode: 'start', status: 'Fail' }), { now: false, goal: true }, en)).toBe('Failed')
    expect(stepNote(step({ mode: 'fail' }), { now: false, goal: false }, en)).toBe('must fail it')
    expect(stepNote(step({ mode: 'start' }), { now: false, goal: true }, en)).toBe('just accept it')
    expect(stepNote(step({}), { now: false, goal: true }, en)).toBe('goal')
    expect(stepNote(step({}), { now: false, goal: false }, en)).toBe('Locked')
  })
})
