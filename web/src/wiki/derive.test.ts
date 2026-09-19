import { describe, expect, it } from 'vitest'
import type { CatalogQuest, CatalogTrader } from '../api/catalog'
import { countByTrader, DEFAULT_CHIPS, filterQuests, initials, makeLookup, orderTraders, sortQuests, toggleMember } from './derive'

function quest(p: Partial<CatalogQuest> & { id: string }): CatalogQuest {
  return {
    name: p.id, description: '', traderId: 't1', side: 'Pmc', factionOnly: null, isVanilla: true, imageUrl: null,
    minLevel: null, requirements: [], prerequisites: [], unlocks: [], objectives: [],
    rewards: { started: [], success: [], fail: [] }, tags: [], ...p,
  }
}
const trader = (id: string, name: string, isVanilla: boolean): CatalogTrader => ({ id, name, avatarUrl: null, isVanilla })

describe('orderTraders', () => {
  it('바닐라는 카탈로그 순서, 그 뒤 모드는 이름순', () => {
    const out = orderTraders({
      b: trader('b', 'Zeta', false), a: trader('a', 'Prapor', true), c: trader('c', 'Alpha', false), d: trader('d', 'Therapist', true),
    })
    expect(out.map((t) => t.id)).toEqual(['a', 'd', 'c', 'b'])
  })
})

describe('countByTrader', () => {
  it('traderId 별 개수', () => {
    expect(countByTrader([quest({ id: '1', traderId: 'x' }), quest({ id: '2', traderId: 'x' }), quest({ id: '3', traderId: 'y' })]))
      .toEqual({ x: 2, y: 1 })
  })
})

describe('initials', () => {
  it('한 단어면 첫 글자, 두 단어 이상이면 두 단어의 첫 글자', () => {
    expect(initials('Prapor')).toBe('P')
    expect(initials('Lightkeeper')).toBe('L')
    expect(initials('Ref Store')).toBe('RS')
    expect(initials('프라포르')).toBe('프')
    expect(initials('  ')).toBe('')
  })
})

describe('filterQuests', () => {
  const qs = [
    quest({ id: 'v-bear', traderId: 'a', isVanilla: true, factionOnly: 'bear', name: 'Debut' }),
    quest({ id: 'v-usec', traderId: 'a', isVanilla: true, factionOnly: 'usec', name: 'Checking' }),
    quest({ id: 'v-none', traderId: 'b', isVanilla: true, name: 'Shootout Picnic' }),
    quest({ id: 'm-none', traderId: 'c', isVanilla: false, name: "Painter's Request" }),
  ]
  const base = { traders: new Set<string>(), chips: DEFAULT_CHIPS, query: '' }
  const ids = (out: CatalogQuest[]) => out.map((q) => q.id)

  it('기본값은 전부', () => expect(ids(filterQuests(qs, base))).toEqual(['v-bear', 'v-usec', 'v-none', 'm-none']))
  it('상인 선택은 포함만', () => expect(ids(filterQuests(qs, { ...base, traders: new Set(['a']) }))).toEqual(['v-bear', 'v-usec']))
  it('바닐라 끔 → 모드만', () => expect(ids(filterQuests(qs, { ...base, chips: { ...DEFAULT_CHIPS, vanilla: false } }))).toEqual(['m-none']))
  it('둘 다 끔 → 빈 리스트', () => expect(filterQuests(qs, { ...base, chips: { ...DEFAULT_CHIPS, vanilla: false, mod: false } })).toEqual([]))
  it('BEAR 전용만', () => expect(ids(filterQuests(qs, { ...base, chips: { ...DEFAULT_CHIPS, bear: true } }))).toEqual(['v-bear']))
  it('USEC 전용만', () => expect(ids(filterQuests(qs, { ...base, chips: { ...DEFAULT_CHIPS, usec: true } }))).toEqual(['v-usec']))
  it('둘 다 켜면 팩션 전용 전부', () => expect(ids(filterQuests(qs, { ...base, chips: { ...DEFAULT_CHIPS, bear: true, usec: true } }))).toEqual(['v-bear', 'v-usec']))
  it('검색은 부분 일치·대소문자 무시', () => {
    expect(ids(filterQuests(qs, { ...base, query: 'OUT' }))).toEqual(['v-none'])
    expect(ids(filterQuests(qs, { ...base, query: "painter's" }))).toEqual(['m-none'])
    expect(ids(filterQuests(qs, { ...base, query: '  ' }))).toHaveLength(4)
  })
})

describe('sortQuests', () => {
  it('minLevel null 은 0 으로 맨 앞, 같은 레벨은 이름순, 원본은 그대로', () => {
    const src = [quest({ id: 'b', minLevel: 2, name: 'B' }), quest({ id: 'n', minLevel: null, name: 'N' }), quest({ id: 'a', minLevel: 2, name: 'A' }), quest({ id: 'z', minLevel: 1, name: 'Z' })]
    expect(sortQuests(src).map((q) => q.id)).toEqual(['n', 'z', 'a', 'b'])
    expect(src[0].id).toBe('b')
  })
})

describe('makeLookup', () => {
  it('상인은 이름 없으면 id, 퀘스트는 없으면 undefined', () => {
    const l = makeLookup({
      sptVersion: '', modVersion: '', generatedAt: '', lang: 'en', rewardIndex: {}, warnings: [],
      traders: { t1: trader('t1', 'Prapor', true) },
      quests: { q1: quest({ id: 'q1', name: 'Debut' }) },
    })
    expect(l.traderName('t1')).toBe('Prapor')
    expect(l.traderName('zzz')).toBe('zzz')
    expect(l.questName('q1')).toBe('Debut')
    expect(l.questName('zzz')).toBeUndefined()
  })
})

describe('toggleMember', () => {
  it('없으면 추가', () => {
    const src = new Set(['a'])
    const out = toggleMember(src, 'b')
    expect(out).toEqual(new Set(['a', 'b']))
    expect(src).toEqual(new Set(['a']))
  })
  it('있으면 제거, 입력은 그대로', () => {
    const src = new Set(['a', 'b'])
    const out = toggleMember(src, 'a')
    expect(out).toEqual(new Set(['b']))
    expect(src).toEqual(new Set(['a', 'b']))
  })
})
