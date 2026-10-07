import { describe, expect, it } from 'vitest'
import type { Catalog, CatalogQuest, Objective, ObjectivePrep, PrepItem } from '../api/catalog'
import type { ProfileProgress, QuestProgress } from '../api/progress'
import {
  aggregateNeeds, countTabByTrader, countTabs,dedupeRows, entryPlace, filterNeedRows, filterProgressQuests, groupByQuest, handoverReady, mapsFromText, placeFinds, isUnreachable, itemKey, itemNeeds,
  mapBrief, mapGroup, mapTabs, mergeSources, missing, objectiveMaps, OTHER_CATEGORY, questCompletion, questTab, sortForTab, raidEntries, raidFinds, raidMapPlan, orderRaidQuests, remaining, rowCategory, searchKey, sortNeedRows,
  type RaidEntry,
} from './derive'

const marker = { tpl: 'ms2000', name: 'MS2000 Marker' }
const jammer = { tpl: 'jam', name: 'Signal Jammer' }
const tagA = { tpl: 'tagA', name: 'BEAR dogtag' }
const tagB = { tpl: 'tagB', name: 'BEAR dogtag' }

function prep(p: Partial<ObjectivePrep> = {}): ObjectivePrep {
  return {
    maps: [], mapKeys: [], item: null, weapons: [], calibers: [], weaponMods: [], equipment: [], forbiddenEquipment: [],
    oneRaid: false, exitStatuses: [], exitName: null, ...p,
  }
}

function item(p: Partial<PrepItem> = {}): PrepItem {
  return {
    action: 'handover', items: [marker], count: 1, foundInRaid: false,
    minDurability: null, maxDurability: null, dogtagLevel: null, plantSeconds: null, ...p,
  }
}

function obj(id: string, conditionType: string, p: ObjectivePrep | null = null, targetCount: number | null = null): Objective {
  return { conditionId: id, conditionType, text: id, targetName: null, targetCount, prep: p }
}

function quest(id: string, objectives: Objective[], p: Partial<CatalogQuest> = {}): CatalogQuest {
  return {
    id, name: id, description: '', traderId: 't1', side: 'Pmc', factionOnly: null, isVanilla: true, modName: null, imageUrl: null,
    minLevel: null, location: null, locationKey: null, requirements: [], prerequisites: [], unlocks: [], failsWhen: [], objectives,
    rewards: { started: [], success: [], fail: [] }, tags: [], ...p,
  }
}

function catalog(quests: CatalogQuest[]): Catalog {
  return {
    sptVersion: '', modVersion: '', generatedAt: '', lang: 'en', traders: {}, rewardIndex: {}, warnings: [], itemCategories: [], itemCategoryOf: {},
    quests: Object.fromEntries(quests.map((q) => [q.id, q])),
  }
}

const qp = (status: string, objectives: QuestProgress['objectives'] = {}, lockReasons: QuestProgress['lockReasons'] = []): QuestProgress =>
  ({ status, startTime: null, finishTime: null, lockReasons, objectives })

function progress(quests: Record<string, QuestProgress>, inventory: ProfileProgress['inventory'] = {}): ProfileProgress {
  return { profileId: 'p', nickname: 'p', level: 1, side: 'Usec', isActive: true, quests, traderStats: {}, inventory, warnings: [] }
}

describe('remaining', () => {
  it('max(0, target − current), done 이면 0', () => {
    const o = obj('a', 'HandoverItem', prep({ item: item({ count: 5 }) }))
    expect(remaining(o, { current: 2, target: 5, done: false })).toBe(3)
    expect(remaining(o, { current: 7, target: 5, done: false })).toBe(0)
    expect(remaining(o, { current: 0, target: 5, done: true })).toBe(0)
  })
  it('카운터가 없으면 준비물 수 → targetCount 순으로 target 을 잡는다', () => {
    expect(remaining(obj('a', 'HandoverItem', prep({ item: item({ count: 4 }) })), undefined)).toBe(4)
    expect(remaining(obj('b', 'CounterCreator', null, 15), undefined)).toBe(15)
  })
})

describe('isUnreachable', () => {
  it('다른 진영 전용, 이미 끝난 선행에 다른 결과를 요구하면 도달 불가', () => {
    expect(isUnreachable(qp('Locked', {}, [{ kind: 'faction', need: 'bear' }]))).toBe(true)
    expect(isUnreachable(qp('Locked', {}, [{ kind: 'quest', questId: 'x', needStatuses: ['Fail'], currentStatus: 'Success' }]))).toBe(true)
  })
  it('선행이 아직 진행 중이면 도달 가능', () => {
    expect(isUnreachable(qp('Locked', {}, [{ kind: 'quest', questId: 'x', needStatuses: ['Success'], currentStatus: 'Started' }]))).toBe(false)
    expect(isUnreachable(qp('Locked', {}, [{ kind: 'level', need: 20, compare: '>=', current: 10 }]))).toBe(false)
  })
})

describe('itemKey', () => {
  it('대체 목록은 tpl 집합을 정렬해 하나의 키로', () => {
    expect(itemKey(item({ items: [tagB, tagA, tagB] }))).toBe('tagA|tagB')
    expect(itemKey(item({ items: [marker] }))).toBe('ms2000')
  })
})

describe('itemNeeds + aggregateNeeds', () => {
  // 광신도 2부: MS2000 을 목표 3개로 나눠 요구. 다른 퀘스트도 2개 요구.
  const cult = quest('cult', [1, 2, 3].map((n) => obj(`c${n}`, 'PlaceBeacon', prep({ item: item({ action: 'plant' }) }))))
  const other = quest('other', [obj('o1', 'PlaceBeacon', prep({ item: item({ action: 'plant', count: 2 }) }))])
  const done = quest('done', [obj('d1', 'HandoverItem', prep({ item: item({ count: 9 }) }))])
  const bear = quest('bear', [obj('b1', 'HandoverItem', prep({ item: item({ count: 9 }) }))])
  const cat = catalog([cult, other, done, bear])
  const prog = progress({
    cult: qp('Started', { c1: { current: 1, target: 1, done: true }, c2: { current: 0, target: 1, done: false }, c3: { current: 0, target: 1, done: false } }),
    other: qp('Locked'),
    done: qp('Success'),
    bear: qp('Locked', {}, [{ kind: 'faction', need: 'bear' }]),
  }, { ms2000: { count: 1, fir: 0 } })

  it('같은 tpl 의 남은 수를 전부 합친 뒤 보유와 비교 — 끝난·도달 불가 퀘스트는 제외', () => {
    const rows = aggregateNeeds(itemNeeds(cat, prog), prog.inventory!)
    expect(rows).toHaveLength(1)
    expect(rows[0]).toMatchObject({ key: 'ms2000', need: 4, needFir: 0, have: 1, haveFir: 0 })
    expect(rows[0].sources.map((s) => s.conditionId)).toEqual(['c2', 'c3', 'o1'])
    expect(missing(rows[0])).toBe(3)
  })

  it('대체 목록은 변종 보유를 합쳐 센다', () => {
    const q = quest('tags', [obj('t', 'HandoverItem', prep({ item: item({ items: [tagA, tagB], count: 5, foundInRaid: true }) }))])
    const rows = aggregateNeeds(itemNeeds(catalog([q]), progress({ tags: qp('Started') })), { tagA: { count: 2, fir: 2 }, tagB: { count: 3, fir: 3 } })
    expect(rows[0]).toMatchObject({ need: 5, needFir: 5, have: 5, haveFir: 5 })
    expect(missing(rows[0])).toBe(0)
  })
})

describe('missing', () => {
  it('FIR 요구분은 FIR 보유로만, 나머지는 남은 보유로', () => {
    expect(missing({ need: 5, needFir: 3, have: 4, haveFir: 1 })).toBe(2 + 0)
    expect(missing({ need: 5, needFir: 0, have: 4, haveFir: 0 })).toBe(1)
    expect(missing({ need: 3, needFir: 3, have: 10, haveFir: 0 })).toBe(3)
    expect(missing({ need: 4, needFir: 2, have: 3, haveFir: 3 })).toBe(1)
  })
})

describe('mergeSources', () => {
  it('같은 퀘스트·행동·FIR 은 합치고 나머지는 따로 둔다', () => {
    const src = (questId: string, conditionId: string, action: 'plant' | 'handover', count: number, fir = false) =>
      ({ questId, conditionId, action, count, fir })
    const merged = mergeSources([
      src('q1', 'c1', 'plant', 1), src('q2', 'c2', 'plant', 1), src('q1', 'c3', 'plant', 1),
      src('q1', 'c4', 'handover', 2), src('q1', 'c5', 'handover', 3, true),
    ])
    expect(merged.map((s) => [s.questId, s.action, s.count, s.fir])).toEqual([
      ['q1', 'plant', 2, false], ['q2', 'plant', 1, false], ['q1', 'handover', 2, false], ['q1', 'handover', 3, true],
    ])
  })
})

describe('sortNeedRows / filterNeedRows', () => {
  const row = (key: string, name: string, need: number, have: number, needFir = 0) =>
    ({ key, items: [{ tpl: key, name }], need, needFir, have, haveFir: 0, sources: [] })
  const rows = [row('a', 'Alpha', 1, 1), row('b', 'Bravo', 5, 0), row('c', 'T H I C C Item case', 2, 1, 2)]

  it('부족분 많은 순', () => {
    expect(sortNeedRows(rows).map((r) => r.key)).toEqual(['b', 'c', 'a'])
  })
  it('필터와 공백 무시 검색', () => {
    expect(filterNeedRows(rows, 'missing', '').map((r) => r.key)).toEqual(['b', 'c'])
    expect(filterNeedRows(rows, 'owned', '').map((r) => r.key)).toEqual(['a', 'c'])
    expect(filterNeedRows(rows, 'fir', '').map((r) => r.key)).toEqual(['c'])
    expect(filterNeedRows(rows, 'all', 'thicc').map((r) => r.key)).toEqual(['c'])
    expect(searchKey('T H I C C')).toBe('thicc')
  })
  it('카테고리: 대안 중 아는 첫 아이템을 따르고, 모르면 other', () => {
    const categoryOf = { a: 'barter', c: 'gear' }
    expect(rowCategory({ items: [{ tpl: 'x', name: 'X' }, { tpl: 'c', name: 'C' }] }, categoryOf)).toBe('gear')
    expect(filterNeedRows(rows, 'all', '', 'barter', categoryOf).map((r) => r.key)).toEqual(['a'])
    expect(filterNeedRows(rows, 'missing', '', OTHER_CATEGORY, categoryOf).map((r) => r.key)).toEqual(['b'])
  })
})

describe('mapGroup / objectiveMaps', () => {
  it('변종 맵은 한 탭으로, develop 은 버린다', () => {
    expect(mapGroup('factory4_night')).toBe('factory4_day')
    expect(mapGroup('Sandbox_high')).toBe('sandbox')
    expect(mapGroup('laboratory_dark')).toBe('laboratory')
    expect(mapGroup('develop')).toBeNull()
    expect(mapGroup('woods')).toBe('woods')
  })
  it('목표에 맵이 없으면 퀘스트 맵, 둘 다 없으면 어느 맵이든', () => {
    const q = quest('q', [], { locationKey: 'shoreline' })
    expect(objectiveMaps(q, obj('a', 'PlaceBeacon', prep({ mapKeys: ['factory4_day', 'factory4_night'] })))).toEqual(['factory4_day'])
    expect(objectiveMaps(q, obj('b', 'PlaceBeacon', prep()))).toEqual(['shoreline'])
    expect(objectiveMaps(quest('r', []), obj('c', 'CounterCreator'))).toEqual([])
  })
})

describe('dedupeRows', () => {
  it('라벨·값이 같은 줄은 한 번만, 다른 줄은 순서대로', () => {
    const raid = { label: 'Raid', names: ['One raid'] }
    expect(dedupeRows([
      raid,
      { label: 'Exit', names: ['Survived · Transit'] },
      raid,
      { label: 'Exit', names: ['Transit · TRANSIT01'] },
      { label: 'Exit', names: ['Survived · Transit'] },
    ])).toEqual([raid, { label: 'Exit', names: ['Survived · Transit'] }, { label: 'Exit', names: ['Transit · TRANSIT01'] }])
  })
  it('값 목록의 경계가 달라도 섞이지 않는다', () => {
    expect(dedupeRows([{ label: 'W', names: ['a b', 'c'] }, { label: 'W', names: ['a', 'b c'] }])).toHaveLength(2)
  })
})

describe('mapsFromText / 추정 맵', () => {
  it('영어는 단어 경계, 한국어는 게임 표기', () => {
    expect(mapsFromText('삼림(Woods)에 있는 첫 번째 종교의식 현장에 MS2000 마커 설치하기')).toEqual(['woods'])
    expect(mapsFromText('Stash the AK-50 body at the specified spot on Customs')).toEqual(['bigmap'])
    expect(mapsFromText('Scout the intersection of Mira Ave and the overpass on Ground Zero')).toEqual(['sandbox'])
    expect(mapsFromText('Use the transit from Ground Zero to Streets of Tarkov')).toEqual(['tarkovstreets', 'sandbox'])
    expect(mapsFromText('Locate the checkpoint with seized cargo')).toEqual([])
    expect(mapsFromText('Hand over the Lab journal')).toEqual([])            // 아이템 이름 속 Lab
    expect(mapsFromText('keep them in reserve')).toEqual([])                 // 소문자 일반 명사
  })
  it('데이터에 맵이 없는 목표만 문장으로 채우고 표시한다', () => {
    const cult = quest('cult', [
      obj('w', 'PlaceBeacon', prep({ item: item({ action: 'plant' }) })),
      obj('s', 'PlaceBeacon', prep({ item: item({ action: 'plant' }) })),
      obj('k', 'CounterCreator', prep({ mapKeys: ['woods'] })),
    ])
    cult.objectives[0].text = '삼림(Woods)에 있는 첫 번째 현장'
    cult.objectives[1].text = '해안선(Shoreline)에 있는 현장'
    cult.objectives[2].text = 'Kill on Customs'   // 데이터(woods)가 이긴다
    const e = raidEntries(catalog([cult]), progress({ cult: qp('Started') }))
    expect(e.map((x) => [x.objective.conditionId, x.maps, x.inferred]))
      .toEqual([['w', ['woods'], true], ['s', ['shoreline'], true], ['k', ['woods'], false]])
    expect(e.map((x) => entryPlace(x, 'shoreline'))).toEqual(['elsewhere', 'here', 'elsewhere'])
    expect(entryPlace({ ...e[0], maps: [] }, 'woods')).toBe('unknown')
  })
})

describe('raidEntries / mapTabs / mapBrief', () => {
  const cult = quest('cult', [
    obj('c1', 'PlaceBeacon', prep({ item: item({ action: 'plant', count: 1 }) })),
    obj('c2', 'PlaceBeacon', prep({ item: item({ action: 'plant', count: 1 }) })),
    obj('h', 'HandoverItem', prep({ item: item({ items: [jammer] }) })),
  ], { locationKey: 'woods' })
  const kills = quest('kills', [
    obj('k', 'CounterCreator', prep({ weapons: [{ tpl: 'svd', name: 'SVD' }], oneRaid: true }), 15),
  ])
  const locked = quest('locked', [obj('l', 'CounterCreator', prep({ mapKeys: ['woods'] }))])
  const cat = catalog([cult, kills, locked])
  const prog = progress({
    cult: qp('Started', { c1: { current: 0, target: 1, done: false }, c2: { current: 0, target: 1, done: false }, h: { current: 0, target: 1, done: false } }),
    kills: qp('Started', { k: { current: 7, target: 15, done: false } }),
    locked: qp('Locked'),
  }, { ms2000: { count: 1, fir: 1 } })
  const entries = raidEntries(cat, prog)

  it('진행 중 퀘스트의 레이드 목표만 — 제출 목표와 잠긴 퀘스트는 빠진다', () => {
    expect(entries.map((e) => e.objective.conditionId)).toEqual(['c1', 'c2', 'k'])
  })
  it('퀘스트별로 묶는다 — 처음 나온 순, 퀘스트 안은 목표 순', () => {
    const groups = groupByQuest([entries[0], entries[2], entries[1]])
    expect(groups.map((g) => [g.quest.id, g.entries.map((e) => e.objective.conditionId)]))
      .toEqual([['cult', ['c1', 'c2']], ['kills', ['k']]])
  })
  it('탭: 알려진 맵은 0 이어도 고정 순서, 어느 맵이든 목표는 세지 않는다', () => {
    const tabs = mapTabs(entries)
    expect(tabs[0]).toEqual({ key: 'bigmap', count: 0 })
    expect(tabs.find((t) => t.key === 'woods')?.count).toBe(2)
    expect(mapTabs([{ ...entries[0], maps: ['modmap'] }]).at(-1)).toEqual({ key: 'modmap', count: 1 })
    // 모드 맵은 목표가 없어도 서버에 있으면(catalog.exits 키) 탭이 생긴다 — 변종 키는 한 탭으로
    const withServer = mapTabs(entries, ['icebreaker', 'factory4_night', 'sandbox_high'])
    expect(withServer.at(-1)).toEqual({ key: 'icebreaker', count: 0 })
    expect(withServer).toHaveLength(tabs.length + 1)
  })
  it('브리핑: 설치 아이템 합산, 어느 맵이든 목표 포함, 장비·특수 조건', () => {
    const b = mapBrief(entries, 'woods', prog.inventory!)
    expect(b.here.map((e) => e.objective.conditionId)).toEqual(['c1', 'c2'])
    expect(b.anywhere.map((e) => e.objective.conditionId)).toEqual(['k'])
    expect(b.bring).toHaveLength(1)
    expect(b.bring[0]).toMatchObject({ key: 'ms2000', need: 2, have: 1 })
    expect(b.bringAnywhere).toEqual([])
    expect(b.gear.map((e) => e.objective.conditionId)).toEqual(['k'])
    expect(b.special.map((e) => e.objective.conditionId)).toEqual(['k'])
    expect(mapBrief(entries, 'bigmap', {}).here).toEqual([])
  })
  it('지도 번호: 이 맵 좌표가 있는 퀘스트만 목록 순서로 1부터, 한 퀘스트의 목표는 같은 번호', () => {
    const at = (map: string) => [{ map, points: [{ x: 0, y: 0, z: 0 }] }]
    const a = quest('a', [{ ...obj('a1', 'VisitPlace'), locations: at('factory4_night') }, { ...obj('a2', 'VisitPlace'), locations: at('factory4_day') }])
    const b = quest('b', [{ ...obj('b1', 'VisitPlace'), locations: at('woods') }])
    const c = quest('c', [obj('c1', 'VisitPlace'), { ...obj('c2', 'VisitPlace'), locations: at('factory4_day') }])
    const here: RaidEntry[] = [a, b, c].flatMap((q) => q.objectives.map((o) => ({ quest: q, objective: o, progress: undefined, maps: ['factory4_day'], inferred: false })))
    const plan = raidMapPlan(here, 'factory4_day')
    expect([...plan.numbers]).toEqual([['a', 1], ['c', 2]])
    expect(plan.items.map((i) => [i.n, i.objective.conditionId])).toEqual([[1, 'a1'], [1, 'a2'], [2, 'c2']])
  })
  it('퀘스트 순서: 지도에 찍히는 것 → 카운터 있는 것(진행률 높은 순) → 나머지(이름순)', () => {
    const at = [{ map: 'woods', points: [{ x: 0, y: 0, z: 0 }] }]
    const entry = (q: CatalogQuest, o: Objective, current: number, target: number | null): RaidEntry =>
      ({ quest: q, objective: o, progress: { current, target, done: false }, maps: ['woods'], inferred: false })
    const zebra = quest('zebra', [obj('z', 'VisitPlace')])
    const apple = quest('apple', [obj('a', 'VisitPlace')])
    const low = quest('low', [obj('l', 'CounterCreator')])
    const high = quest('high', [obj('h1', 'CounterCreator'), obj('h2', 'CounterCreator')])
    const mapped = quest('mapped', [{ ...obj('m', 'VisitPlace'), locations: at }])
    const here = [
      entry(zebra, zebra.objectives[0], 0, 1),          // target 1 은 카운터로 치지 않는다
      entry(low, low.objectives[0], 2, 20),
      entry(apple, apple.objectives[0], 0, null),
      entry(high, high.objectives[0], 5, 10), entry(high, high.objectives[1], 10, 10),
      entry(mapped, mapped.objectives[0], 0, 1),
    ]
    expect(orderRaidQuests(here, 'woods').map((g) => g.quest.id)).toEqual(['mapped', 'high', 'low', 'apple', 'zebra'])
    expect([...raidMapPlan(here, 'woods').numbers]).toEqual([['mapped', 1]])
  })
  it('맵 없는 설치 목표의 아이템은 따로 — 맵별 목록에 섞지 않는다', () => {
    const noMap = quest('nomap', [obj('n', 'PlaceBeacon', prep({ item: item({ action: 'plant', items: [jammer] }) }))])
    const e = raidEntries(catalog([noMap]), progress({ nomap: qp('Started') }))
    const b = mapBrief(e, 'woods', {})
    expect(b.bring).toEqual([])
    expect(b.bringAnywhere.map((r) => r.key)).toEqual(['jam'])
  })
})

describe('raidFinds', () => {
  it('진행 중 퀘스트의 제출 목표 중 보유로 못 채우는 것만, 부족분 순', () => {
    const cowboy = { tpl: 'cowboy', name: 'Cowboy hat' }
    const hats = quest('hats', [
      obj('f', 'FindItem', null, 2),
      obj('h1', 'HandoverItem', prep({ item: item({ items: [cowboy], count: 2, foundInRaid: true }) })),
      obj('h2', 'HandoverItem', prep({ item: item({ items: [jammer], count: 3 }) })),
      obj('p', 'PlaceBeacon', prep({ item: item({ action: 'plant', count: 5 }) })),
    ])
    const later = quest('later', [obj('x', 'HandoverItem', prep({ item: item({ items: [cowboy], count: 9 }) }))])
    const prog = progress({
      // 찾기 카운터는 0 이지만 하나는 이미 제출 — 남은 제출 1 이 기준
      hats: qp('Started', { f: { current: 0, target: 2, done: false }, h1: { current: 1, target: 2, done: false } }),
      later: qp('Locked'),
    })
    const rows = raidFinds(catalog([hats, later]), prog, { cowboy: { count: 3, fir: 0 }, jam: { count: 1, fir: 0 } })
    expect(rows.map((r) => [r.key, r.need, missing(r)])).toEqual([['jam', 3, 2], ['cowboy', 1, 1]])
  })
  it('맵: 요구 퀘스트의 찾기 목표 문장에서 — 이 맵 → 맵 미상 → 다른 맵 순', () => {
    const letter = { tpl: 'letter', name: 'Registered letter' }
    const pump = { tpl: 'pump', name: 'Pump data' }
    const hat = { tpl: 'hat', name: 'Cowboy hat' }
    const hand = (id: string, it: typeof hat) => obj(id, 'HandoverItem', prep({ item: item({ items: [it] }) }))
    const find = (id: string, text: string) => ({ ...obj(id, 'FindItem'), text })
    const cat = catalog([
      quest('mail', [find('f1', 'Find the letter on Streets of Tarkov'), hand('h1', letter)]),
      quest('pumps', [find('f2', '등대(Lighthouse) 지역의 펌프장에서 정보 찾기'), hand('h2', pump)]),
      quest('hats', [find('f3', '레이드에서 [카우보이 모자] 획득하기'), hand('h3', hat)]),
    ])
    const prog = progress({ mail: qp('Started'), pumps: qp('Started'), hats: qp('Started') })
    const placed = placeFinds(raidFinds(cat, prog, {}), cat, 'lighthouse')
    expect(placed.map((p) => [p.row.key, p.maps, p.place])).toEqual([
      ['pump', ['lighthouse'], 'here'],
      ['hat', [], 'unknown'],
      ['letter', ['tarkovstreets'], 'elsewhere'],
    ])
  })
  it('돈 제출은 구해 올 것이 아니다', () => {
    const euro = { tpl: '569668774bdc2da2298b4568', name: 'Euros' }
    const q = quest('mentor', [obj('m', 'HandoverItem', prep({ item: item({ items: [euro], count: 50000 }) }))])
    expect(raidFinds(catalog([q]), progress({ mentor: qp('Started') }), {})).toEqual([])
  })
})

describe('handoverReady', () => {
  it('퀘스트 안에서 합산해 보유로 채울 수 있는 제출 목표 수', () => {
    const q = quest('q', [
      obj('a', 'HandoverItem', prep({ item: item({ count: 2 }) })),
      obj('b', 'HandoverItem', prep({ item: item({ count: 2 }) })),
      obj('c', 'HandoverItem', prep({ item: item({ items: [jammer], count: 1, foundInRaid: true }) })),
    ])
    const cat = catalog([q])
    const started = progress({ q: qp('Started') })
    // MS2000 필요 4 중 보유 3 → a·b 둘 다 불가, jammer FIR 1 → c 가능
    expect(handoverReady(cat, started, { ms2000: { count: 3, fir: 3 }, jam: { count: 1, fir: 1 } }))
      .toEqual([{ quest: q, ready: 1, total: 3 }])
    expect(handoverReady(cat, started, { jam: { count: 1, fir: 0 } })).toEqual([])
  })
})

describe('questTab / countTabs / filterProgressQuests', () => {
  it('상태 → 탭', () => {
    expect(questTab('Started')).toBe('active')
    expect(questTab('AvailableForFinish')).toBe('active')
    expect(questTab('AvailableForStart')).toBe('available')
    expect(questTab('Locked')).toBe('locked')
    expect(questTab('Success')).toBe('done')
    expect(questTab('FailRestartable')).toBe('failed')
  })
  it('프로필에 없는 퀘스트는 잠김으로 센다, 검색은 공백 무시', () => {
    const cat = catalog([quest('a', [], { name: 'Gunsmith - Part 1' }), quest('b', []), quest('c', [], { traderId: 't2' })])
    const prog = progress({ a: qp('Started'), c: qp('Started') })
    expect(countTabs(cat, prog)).toEqual({ active: 2, available: 0, locked: 1, done: 0, failed: 0 })
    expect(filterProgressQuests(cat, prog, { tab: 'active', traderIds: new Set(), query: 'gun smith' }).map((q) => q.id)).toEqual(['a'])
    expect(filterProgressQuests(cat, prog, { tab: 'active', traderIds: new Set(['t2']), query: '' }).map((q) => q.id)).toEqual(['c'])
    expect(filterProgressQuests(cat, prog, { tab: 'active', traderIds: new Set(['t1', 't2']), query: '' }).map((q) => q.id).sort()).toEqual(['a', 'c'])
  })
  it('진행 중: 완료 보고 대기 → 진행률 높은 순 → 최소 레벨', () => {
    const cat = catalog([
      quest('low', [obj('a', 'CounterCreator')], { minLevel: 1 }),
      quest('high', [obj('a', 'CounterCreator')], { minLevel: 30 }),
      quest('ready', [obj('a', 'CounterCreator')], { minLevel: 40 }),
      quest('tie', [obj('a', 'CounterCreator')], { minLevel: 5 }),
    ])
    const prog = progress({
      low: qp('Started', { a: { current: 1, target: 10, done: false } }),
      high: qp('Started', { a: { current: 8, target: 10, done: false } }),
      ready: qp('AvailableForFinish', { a: { current: 10, target: 10, done: true } }),
      tie: qp('Started', { a: { current: 1, target: 10, done: false } }),
    })
    expect(sortForTab(Object.values(cat.quests), prog, 'active').map((q) => q.id)).toEqual(['ready', 'high', 'low', 'tie'])
  })
  it('진행률: 카운터는 비율, 카운터 없는 목표는 끝났으면 1', () => {
    const q = quest('q', [obj('a', 'CounterCreator'), obj('b', 'FindItem')])
    expect(questCompletion(q, qp('Started', { a: { current: 5, target: 10, done: false }, b: { current: 0, target: null, done: true } }))).toBe(0.75)
    expect(questCompletion(quest('e', []), qp('Started'))).toBe(0)
  })
  it('잠김: 모자란 레벨 적은 순 → 남은 선행 단계 적은 순 → 사유 모름 → 도달 불가', () => {
    const cat = catalog([
      quest('far', [], { minLevel: 1 }),
      quest('near', [], { minLevel: 50 }),
      quest('one', [], { minLevel: 20 }),
      quest('unknown', [], { minLevel: 1 }),
      quest('never', [], { minLevel: 1 }),
    ])
    const prog = progress({
      far: qp('Locked', {}, [{ kind: 'level', need: 30, compare: '>=', current: 10 }, { kind: 'quest', questId: 'x', needStatuses: ['Success'], currentStatus: 'Locked' }]),
      near: qp('Locked', {}, [{ kind: 'quest', questId: 'x', needStatuses: ['Success'], currentStatus: 'Started' }]),
      one: qp('Locked', {}, [{ kind: 'level', need: 30, compare: '>=', current: 10 }]),
      never: qp('Locked', {}, [{ kind: 'faction', need: 'bear' }]),
    })
    expect(sortForTab(Object.values(cat.quests), prog, 'locked').map((q) => q.id)).toEqual(['near', 'one', 'far', 'unknown', 'never'])
  })
  it('잠김: 전부 Locked 인 새 프로필에서도 선행 사슬을 따라 단계 수로 줄 세운다', () => {
    // a(열림 직전) ← b ← c, d 는 c 를 "진행 중" 만 요구, e 는 a·c 둘 다 필요(긴 쪽). lv 는 레벨 2 만 남았지만 선행 사슬의 a 보다 뒤
    const cat = catalog(['lv', 'e', 'd', 'c', 'b', 'a'].map((id) => quest(id, [], { name: id })))
    const lockOn = (id: string, need = ['Success']) => ({ kind: 'quest' as const, questId: id, needStatuses: need, currentStatus: 'Locked' })
    const prog = progress({
      a: qp('Locked', {}, [lockOn('z', ['Success'])]),
      lv: qp('Locked', {}, [{ kind: 'level', need: 2, compare: '>=', current: 1 }]),
      b: qp('Locked', {}, [lockOn('a')]),
      c: qp('Locked', {}, [lockOn('b')]),
      d: qp('Locked', {}, [lockOn('c', ['Started', 'Success'])]),
      e: qp('Locked', {}, [lockOn('a'), lockOn('c')]),
    })
    expect(sortForTab(Object.values(cat.quests), prog, 'locked').map((q) => q.id)).toEqual(['a', 'b', 'c', 'd', 'e', 'lv'])
  })
  it('완료: 끝난 시각 최신순, 시각 없으면 맨 뒤', () => {
    const cat = catalog([quest('old', []), quest('none', []), quest('new', [])])
    const at = (finishTime: string | null): QuestProgress => ({ ...qp('Success'), finishTime })
    const prog = progress({ old: at('2026-09-01T00:00:00+00:00'), none: at(null), new: at('2026-09-30T00:00:00+00:00') })
    expect(sortForTab(Object.values(cat.quests), prog, 'done').map((q) => q.id)).toEqual(['new', 'old', 'none'])
  })
  it('상인별 개수는 고른 탭 안에서만 센다', () => {
    const cat = catalog([quest('a', []), quest('b', []), quest('c', [], { traderId: 't2' })])
    const prog = progress({ a: qp('Started'), c: qp('Started') })
    expect(countTabByTrader(cat, prog, 'active')).toEqual({ t1: 1, t2: 1 })
    expect(countTabByTrader(cat, prog, 'locked')).toEqual({ t1: 1 })
  })
  it('출처 모드를 고르면 탭·상인 개수와 목록이 그 모드 퀘스트만', () => {
    const cat = catalog([
      quest('v', []),
      quest('ice-a', [], { isVanilla: false, modName: 'ManimalIcebreaker' }),
      quest('ice-b', [], { isVanilla: false, modName: 'ManimalIcebreaker', traderId: 't2' }),
      quest('artem', [], { isVanilla: false, modName: 'WTT-Artem' }),
    ])
    const prog = progress({ v: qp('Started'), 'ice-a': qp('Started'), artem: qp('Started') })
    const mods = new Set(['ManimalIcebreaker'])
    expect(countTabs(cat, prog, mods)).toEqual({ active: 1, available: 0, locked: 1, done: 0, failed: 0 })
    expect(countTabByTrader(cat, prog, 'locked', mods)).toEqual({ t2: 1 })
    expect(filterProgressQuests(cat, prog, { tab: 'active', traderIds: new Set(), query: '', mods }).map((q) => q.id)).toEqual(['ice-a'])
    expect(filterProgressQuests(cat, prog, { tab: 'active', traderIds: new Set(), query: '', mods: new Set() })).toHaveLength(3)
  })
})
