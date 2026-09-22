import { describe, expect, it } from 'vitest'
import type { CatalogQuest, CatalogTrader } from '../api/catalog'
import { assignModColors, chainRank, countByTrader, DEFAULT_CHIPS, DEFAULT_SORT, filterQuests, initials, makeLookup, MOD_COLOR_COUNT, orderTraders, sortQuests, toggleMember } from './derive'

function quest(p: Partial<CatalogQuest> & { id: string }): CatalogQuest {
  return {
    name: p.id, description: '', traderId: 't1', side: 'Pmc', factionOnly: null, isVanilla: true, modName: null, imageUrl: null,
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
  const src = () => [
    quest({ id: 'b', minLevel: 2, name: 'B' }),
    quest({ id: 'n', minLevel: null, name: 'N' }),
    quest({ id: 'a', minLevel: 2, name: 'A' }),
    quest({ id: 'z', minLevel: 1, name: 'Z' }),
    quest({ id: 'm', minLevel: null, name: 'M' }),
  ]

  it('레벨순: minLevel null 은 맨 뒤, 같은 레벨은 이름순', () => {
    expect(sortQuests(src(), 'level').map((q) => q.id)).toEqual(['z', 'a', 'b', 'm', 'n'])
  })
  it('이름순: 레벨을 무시하고 이름만 본다', () => {
    expect(sortQuests(src(), 'name').map((q) => q.id)).toEqual(['a', 'b', 'm', 'n', 'z'])
  })
  it('기준을 생략하면 기본값(레벨순)', () => {
    expect(sortQuests(src()).map((q) => q.id)).toEqual(sortQuests(src(), DEFAULT_SORT).map((q) => q.id))
    expect(DEFAULT_SORT).toBe('level')
  })
  it('원본은 그대로', () => {
    const input = src()
    sortQuests(input, 'name')
    expect(input[0].id).toBe('b')
  })
})

describe('chainRank', () => {
  /** 순위 맵을 id 배열로 펴서 읽기 쉽게. */
  const order = (qs: CatalogQuest[]) => [...chainRank(qs).entries()].sort((a, b) => a[1] - b[1]).map(([id]) => id)

  it('선행은 언제나 후속보다 앞', () => {
    const qs = [
      quest({ id: 'c', prerequisites: ['b'] }),
      quest({ id: 'a' }),
      quest({ id: 'b', prerequisites: ['a'] }),
    ]
    expect(order(qs)).toEqual(['a', 'b', 'c'])
  })

  it('체인을 끝까지 따라간 뒤 다음 체인으로 (깊이 우선)', () => {
    const qs = [
      quest({ id: 'r1', name: 'A' }), quest({ id: 'r1-x', name: 'X', prerequisites: ['r1'] }),
      quest({ id: 'r2', name: 'B' }), quest({ id: 'r2-y', name: 'Y', prerequisites: ['r2'] }),
    ]
    expect(order(qs)).toEqual(['r1', 'r1-x', 'r2', 'r2-y'])   // 너비 우선이면 r1,r2,r1-x,r2-y
  })

  it('갈림길은 레벨순, 레벨 없음은 0 취급(= 앞)', () => {
    const qs = [
      quest({ id: 'root', name: 'root' }),
      quest({ id: 'lv5', name: 'B', minLevel: 5, prerequisites: ['root'] }),
      quest({ id: 'none', name: 'C', minLevel: null, prerequisites: ['root'] }),
      quest({ id: 'lv2', name: 'A', minLevel: 2, prerequisites: ['root'] }),
    ]
    expect(order(qs)).toEqual(['root', 'none', 'lv2', 'lv5'])
  })

  it('갈림길에서 레벨이 같으면 이름순', () => {
    const qs = [
      quest({ id: 'root', name: 'root' }),
      quest({ id: 'z', name: 'Z', minLevel: 3, prerequisites: ['root'] }),
      quest({ id: 'a', name: 'A', minLevel: 3, prerequisites: ['root'] }),
    ]
    expect(order(qs)).toEqual(['root', 'a', 'z'])
  })

  it('루트도 같은 기준으로 정렬 — 레벨 없는 루트가 레벨 10 루트보다 앞', () => {
    const qs = [quest({ id: 'lv10', name: 'A', minLevel: 10 }), quest({ id: 'none', name: 'Z', minLevel: null })]
    expect(order(qs)).toEqual(['none', 'lv10'])
  })

  it('선행이 여럿이면 마지막 선행이 나온 뒤에야 배출', () => {
    const qs = [
      quest({ id: 'a', name: 'A' }),
      quest({ id: 'b', name: 'B', minLevel: 9 }),
      quest({ id: 'ab', name: 'AB', prerequisites: ['a', 'b'] }),
    ]
    expect(order(qs)).toEqual(['a', 'b', 'ab'])   // a 를 따라가다 ab 를 만나도 b 전이라 건너뛴다
  })

  it('카탈로그에 없는 선행 id 와 자기참조는 무시', () => {
    const qs = [quest({ id: 'x', prerequisites: ['없는퀘스트', 'x'] })]
    expect(order(qs)).toEqual(['x'])
  })

  it('순환이 있어도 멈추고, 모든 퀘스트가 정확히 한 번씩 나온다', () => {
    const qs = [
      quest({ id: 'ok', name: 'ok' }),
      quest({ id: 'c1', name: 'c1', prerequisites: ['c2'] }),
      quest({ id: 'c2', name: 'c2', prerequisites: ['c1'] }),
    ]
    const out = order(qs)
    expect(out).toHaveLength(3)
    expect(new Set(out)).toEqual(new Set(['ok', 'c1', 'c2']))
    expect(out[0]).toBe('ok')                    // 순환은 정상 체인 뒤로 밀린다
  })

  it('모든 퀘스트에 순위가 매겨진다', () => {
    const qs = [quest({ id: 'a' }), quest({ id: 'b', prerequisites: ['a'] }), quest({ id: 'c' })]
    expect(chainRank(qs).size).toBe(3)
  })
})

describe('sortQuests — 연계순', () => {
  const qs = [
    quest({ id: 'a', name: 'A' }),
    quest({ id: 'b', name: 'B', minLevel: 9, prerequisites: ['a'] }),
    quest({ id: 'c', name: 'C', minLevel: 1, prerequisites: ['b'] }),
  ]
  const rank = chainRank(qs)

  it('rank 순서를 그대로 따른다 (레벨을 주 키로 쓰지 않는다)', () => {
    expect(sortQuests(qs, 'chain', rank).map((q) => q.id)).toEqual(['a', 'b', 'c'])
    expect(sortQuests(qs, 'level').map((q) => q.id)).toEqual(['c', 'b', 'a'])   // 레벨순과 다름을 확인
  })

  it('필터된 부분집합에 적용해도 상대 순서가 유지된다', () => {
    const subset = [qs[2], qs[0]]                                              // c, a 만 보이는 상태
    expect(sortQuests(subset, 'chain', rank).map((q) => q.id)).toEqual(['a', 'c'])
  })

  it('rank 를 안 주면 레벨순으로 폴백', () => {
    expect(sortQuests(qs, 'chain').map((q) => q.id)).toEqual(sortQuests(qs, 'level').map((q) => q.id))
  })

  it('rank 에 없는 퀘스트는 뒤로, 그들끼리는 레벨→이름순', () => {
    const extra = quest({ id: 'x', name: 'X', minLevel: 2 })
    expect(sortQuests([...qs, extra], 'chain', rank).map((q) => q.id)).toEqual(['a', 'b', 'c', 'x'])
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

describe('assignModColors', () => {
  const mod = (id: string, modName: string | null) => quest({ id, modName, isVanilla: modName === null })

  it('모드 이름순으로 1부터 색을 배정', () => {
    const out = assignModColors([mod('q1', 'WTT-Artem'), mod('q2', 'WTT-Armory'), mod('q3', 'acidphantasm')])
    expect(out).toEqual({ 'acidphantasm': 1, 'WTT-Armory': 2, 'WTT-Artem': 3 })
  })

  it('같은 모드의 퀘스트가 여러 개여도 색은 하나', () => {
    const out = assignModColors([mod('q1', 'Alpha'), mod('q2', 'Alpha'), mod('q3', 'Beta')])
    expect(out).toEqual({ Alpha: 1, Beta: 2 })
  })

  it('입력 순서가 달라도 결과가 같다(결정적)', () => {
    const a = assignModColors([mod('q1', 'Zeta'), mod('q2', 'Alpha')])
    const b = assignModColors([mod('q1', 'Alpha'), mod('q2', 'Zeta')])
    expect(a).toEqual(b)
  })

  it('modName 이 null 인 퀘스트는 무시', () => {
    expect(assignModColors([mod('q1', null), mod('q2', null)])).toEqual({})
  })

  it('팔레트보다 모드가 많으면 색이 순환한다', () => {
    const names = Array.from({ length: MOD_COLOR_COUNT + 2 }, (_, i) => `mod-${String(i).padStart(2, '0')}`)
    const out = assignModColors(names.map((n, i) => mod(`q${i}`, n)))
    expect(out[names[0]]).toBe(1)
    expect(out[names[MOD_COLOR_COUNT]]).toBe(1)        // 한 바퀴 돌아 1번으로 복귀
    expect(out[names[MOD_COLOR_COUNT + 1]]).toBe(2)
    expect(Object.values(out).every((n) => n >= 1 && n <= MOD_COLOR_COUNT)).toBe(true)
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
