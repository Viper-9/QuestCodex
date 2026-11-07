import { describe, expect, it } from 'vitest'
import type { CatalogQuest, EnemyRule, Objective, ObjectivePrep, PrepItem } from '../api/catalog'
import { getT } from '../i18n/index'
import { distinctNames, enemyRows, exitText, hasRules, headerMap, itemDetails, optionText, prepCount, questMaps } from './prep'

const ko = getT('kr')
const en = getT('en')

const svds = { tpl: 'a', name: 'SVDS' }
const tkpd = { tpl: 'b', name: 'TKPD 9.3x64 carbine' }
const knife = { tpl: 'k', name: 'Bars 나이프' }

function prep(p: Partial<ObjectivePrep> = {}): ObjectivePrep {
  return {
    maps: [], item: null, weapons: [], calibers: [], weaponMods: [], equipment: [], forbiddenEquipment: [],
    oneRaid: false, exitStatuses: [], exitName: null, ...p,
  }
}

function item(p: Partial<PrepItem> = {}): PrepItem {
  return {
    action: 'handover', items: [knife], count: 5, foundInRaid: true,
    minDurability: null, maxDurability: null, dogtagLevel: null, plantSeconds: null, ...p,
  }
}

const obj = (id: string, p: ObjectivePrep | null): Objective =>
  ({ conditionId: id, conditionType: 'CounterCreator', text: id, targetName: null, targetCount: null, prep: p })

function quest(objectives: Objective[], location: string | null = null): CatalogQuest {
  return {
    id: 'q', name: 'q', description: '', traderId: 't', side: 'Pmc', factionOnly: null, isVanilla: true, modName: null,
    imageUrl: null, minLevel: null, location, requirements: [], prerequisites: [], unlocks: [], failsWhen: [], objectives,
    rewards: { started: [], success: [], fail: [] }, tags: [],
  }
}

describe('prepCount / questMaps', () => {
  it('준비물이 있는 목표 수', () => {
    expect(prepCount(quest([obj('1', prep({ oneRaid: true })), obj('2', null), obj('3', prep({ item: item() }))]))).toBe(2)
  })
  it('prep 필드가 없는 구버전 서버 응답은 준비물 없음으로 (흰 화면 회귀)', () => {
    const old = { ...obj('1', null) } as Partial<Objective>
    delete old.prep
    const q = quest([old as Objective, obj('2', prep({ oneRaid: true }))])
    delete (q as Partial<CatalogQuest>).location
    expect(prepCount(q)).toBe(1)
    expect(questMaps(q)).toEqual([])
  })
  it('퀘스트 맵 + 목표 맵, 중복 제거, 퀘스트 맵이 먼저', () => {
    const q = quest([obj('1', prep({ maps: ['세관', '등대'] })), obj('2', prep({ maps: ['등대'] }))], '등대')
    expect(questMaps(q)).toEqual(['등대', '세관'])
  })
})

describe('itemDetails', () => {
  it('FIR 은 칩으로 따로 그리므로 여기엔 제한 조건만', () => {
    expect(itemDetails(item(), ko)).toEqual([])
    expect(itemDetails(item({ minDurability: 60, maxDurability: 100 }), ko)).toEqual(['내구도 60–100%'])
    expect(itemDetails(item({ minDurability: 60 }), ko)).toEqual(['내구도 60% 이상'])
    expect(itemDetails(item({ maxDurability: 50 }), en)).toEqual(['durability ≤ 50%'])
    expect(itemDetails(item({ dogtagLevel: 15, plantSeconds: 30 }), ko)).toEqual(['도그태그 레벨 15 이상', '설치 30초'])
  })
})

describe('optionText / exitText / hasRules', () => {
  it('묶음은 + 로', () => {
    expect(optionText([svds, tkpd])).toBe('SVDS + TKPD 9.3x64 carbine')
  })
  it('탈출 상태 번역, 모르는 상태는 원문, 탈출구 이름', () => {
    expect(exitText(prep({ exitStatuses: ['Survived', 'Runner', 'Weird'] }), ko)).toBe('생존 · 런스루 · Weird')
    expect(exitText(prep({ exitStatuses: ['Survived'], exitName: '클리모프 거리' }), ko)).toBe('생존 · 클리모프 거리 탈출구')
    expect(exitText(prep(), ko)).toBeNull()
  })
  it('아이템만 있는 목표는 규칙 섹션이 필요 없다', () => {
    expect(hasRules(prep({ item: item() }))).toBe(false)
    expect(hasRules(prep({ maps: ['등대'] }))).toBe(true)
    expect(hasRules(prep({ oneRaid: true }))).toBe(true)
  })
})

describe('distinctNames', () => {
  it('tpl 은 달라도 이름이 같으면 한 번만, 순서 유지 (인식표 TUE/EOD/프레스티지 변종)', () => {
    const tags = [
      { tpl: '1', name: 'BEAR 인식표' }, { tpl: '2', name: 'Dogtag BEAR' }, { tpl: '3', name: 'Dogtag BEAR' },
      { tpl: '4', name: 'USEC 인식표' }, { tpl: '5', name: 'Dogtag USEC' }, { tpl: '6', name: 'Dogtag BEAR' },
    ]
    expect(distinctNames(tags.map((t) => t.name))).toEqual(['BEAR 인식표', 'Dogtag BEAR', 'USEC 인식표', 'Dogtag USEC'])
  })
})

describe('headerMap', () => {
  it('맵이 하나면 이름, 여럿이면 "여러 맵", 없으면 null (호위·안내자처럼 8~12개 나열 방지)', () => {
    expect(headerMap(quest([obj('1', prep({ maps: ['등대'] }))], '등대'), ko)).toBe('맵: 등대')
    expect(headerMap(quest([obj('1', null)], '공장'), ko)).toBe('맵: 공장') // 목표에 맵 조건이 없어도 퀘스트 맵
    expect(headerMap(quest([obj('1', prep({ maps: ['세관'] })), obj('2', prep({ maps: ['삼림'] }))]), ko)).toBe('여러 맵')
    expect(headerMap(quest([obj('1', prep({ maps: ['세관'] }))], '공장'), en)).toBe('Multiple maps')
    expect(headerMap(quest([obj('1', null)]), ko)).toBeNull()
  })
})

const killObj = (id: string, enemies: EnemyRule[], p: ObjectivePrep | null = null): Objective => ({ ...obj(id, p), enemies })
const kills = (target: string, roles: string[] = [], names: string[] = roles): EnemyRule => ({ kind: 'kills', target, roles, names })

describe('enemyRows', () => {
  it('처치 대상만 있는 목표도 준비물 수에 센다', () => {
    expect(prepCount(quest([killObj('1', [kills('Savage', ['bossKilla'])]), killObj('2', [kills('Savage')]), obj('3', null)]))).toBe(2)
  })
  it('역할이 있으면 서버가 정한 이름 그대로', () => {
    const o = killObj('1', [kills('Savage', ['bossTagilla', 'followerTagilla'], ['타길라', '타길라 경호원'])])
    expect(enemyRows(o, ko)).toEqual([{ label: '처치 대상', names: ['타길라', '타길라 경호원'] }])
  })
  it('names 가 없는 구버전 서버 응답은 역할 키를 이름 대신', () => {
    const o = killObj('1', [{ kind: 'kills', target: 'Savage', roles: ['bossKilla'] } as EnemyRule])
    expect(enemyRows(o, en)).toEqual([{ label: 'Kill targets', names: ['bossKilla'] }])
  })
  it('역할이 없으면 진영 설명, 명중은 따로, 모르는 진영은 키 그대로', () => {
    const o = killObj('1', [kills('Savage'), { kind: 'shots', target: 'AnyPmc', roles: [] }, kills('ModSide')])
    expect(enemyRows(o, en)).toEqual([
      { label: 'Kill targets', names: ['Any Scav-side AI (incl. bosses, guards, Raiders, Rogues)'] },
      { label: 'Hit targets', names: ['Any PMC (USEC, BEAR)'] },
      { label: 'Kill targets', names: ['ModSide'] },
    ])
  })
  it('enemies 필드가 없는 구버전 서버 응답은 빈 목록', () => {
    expect(enemyRows(obj('1', null), en)).toEqual([])
    expect(prepCount(quest([obj('1', null)]))).toBe(0)
  })
})
