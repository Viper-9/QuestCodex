import { describe, expect, it } from 'vitest'
import type { Catalog, CatalogQuest, Objective, Requirement } from '../api/catalog'
import type { Holding, ProfileProgress, QuestProgress } from '../api/progress'
import {
  chainStats, chainTiers, COLLECTOR_ID, edgePath, formatChance, itemSource, kappaConds, kappaDone, kappaGraph, kappaItems, kappaLists, kappaTree,
  kappaUnlocks, nothingOpen, traderRows, TREE_DONE_BOX, treeFocus, treeState, type KappaTree,
} from './kappa'

function quest(id: string, p: Partial<CatalogQuest> = {}): CatalogQuest {
  return {
    id, name: id, description: '', traderId: 't1', side: 'Pmc', factionOnly: null, isVanilla: true, modName: null, imageUrl: null,
    minLevel: null, location: null, locationKey: null, requirements: [], prerequisites: [], unlocks: [], failsWhen: [], objectives: [],
    rewards: { started: [], success: [], fail: [] }, tags: [], ...p,
  }
}

const after = (questId: string, ...needStatuses: string[]): Requirement =>
  ({ kind: 'quest', questId, needStatuses, availableAfterSec: 0, resolved: true })

const handover = (conditionId: string, tpl: string, foundInRaid = true): Objective => ({
  conditionId, conditionType: 'HandoverItem', text: '', targetName: null, targetCount: 1,
  prep: {
    maps: [], item: { action: 'handover', items: [{ tpl, name: tpl }], count: 1, foundInRaid, minDurability: null, maxDurability: null, dogtagLevel: null, plantSeconds: null },
    weapons: [], calibers: [], weaponMods: [], equipment: [], forbiddenEquipment: [], oneRaid: false, exitStatuses: [], exitName: null,
  },
})

function catalog(quests: CatalogQuest[]): Catalog {
  return {
    sptVersion: '', modVersion: '', generatedAt: '', lang: 'en', rewardIndex: {}, warnings: [], itemCategories: [], itemCategoryOf: {},
    traders: {
      t1: { id: 't1', name: 'T1', avatarUrl: null, isVanilla: true },
      t2: { id: 't2', name: 'T2', avatarUrl: null, isVanilla: true },
    },
    quests: Object.fromEntries(quests.map((q) => [q.id, q])),
  }
}

const qp = (status: string, lockReasons: QuestProgress['lockReasons'] = [], objectives: QuestProgress['objectives'] = {}): QuestProgress =>
  ({ status, startTime: null, finishTime: null, lockReasons, objectives })

function progress(quests: Record<string, QuestProgress>, level = 10): ProfileProgress {
  return { profileId: 'p', nickname: 'p', level, side: 'Usec', isActive: true, quests, traderStats: {}, inventory: {}, warnings: [] }
}

const collector = (p: Partial<CatalogQuest>) => quest(COLLECTOR_ID, { name: 'Collector', traderId: 't2', ...p })

/** 바닐라형: A → B → C, D 는 Collector 직접 요구. S 는 수락만 요구되는 선행 */
const vanilla = catalog([
  quest('A'),
  quest('S'),
  quest('B', { requirements: [after('A', 'Success'), after('S', 'Started')] }),
  quest('C', { requirements: [after('B', 'Success')] }),
  quest('D', { traderId: 't2' }),
  quest('X'),
  collector({ requirements: [after('C', 'Success'), after('D', 'Success')] }),
])

describe('kappaGraph', () => {
  it('Collector 의 완료 조건 선행을 끝까지 따라가고, 수락만 요구하는 선행과 무관한 퀘스트는 뺀다', () => {
    const g = kappaGraph(vanilla)!
    expect([...g.ids].sort()).toEqual(['A', 'B', 'C', 'D'])
    expect(g.downs.get('A')).toEqual(['B'])
    expect(g.ups.get('C')).toEqual(['B'])
  })

  it('Collector 가 없으면 null', () => {
    expect(kappaGraph(catalog([quest('A')]))).toBeNull()
  })
})

describe('kappaDone', () => {
  const branch = catalog([
    quest('P'),
    quest('Q', { requirements: [after('P', 'Success', 'Fail')] }),
    quest('R'),
    collector({ requirements: [after('Q', 'Success'), after('R', 'Success')] }),
  ])
  const g = kappaGraph(branch)!

  it('뒤 퀘스트가 실패도 인정하면 실패를 완료로 센다', () => {
    expect(g.failOk.has('P')).toBe(true)
    expect([...kappaDone(g, progress({ P: qp('Fail'), Q: qp('Success') }))].sort()).toEqual(['P', 'Q'])
  })

  it('성공만 인정하는 퀘스트의 실패·재시작 가능 실패는 미완료', () => {
    expect(kappaDone(g, progress({ R: qp('Fail') })).has('R')).toBe(false)
    expect(kappaDone(g, progress({ R: qp('FailRestartable') })).has('R')).toBe(false)
  })
})

describe('kappaConds', () => {
  const live = catalog([
    quest('A'),
    collector({
      requirements: [
        { kind: 'level', value: 40, compare: '>=' },
        { kind: 'traderLoyalty', traderId: 't1', value: 4, compare: '>=' },
        { kind: 'traderLoyalty', traderId: 't2', value: 4, compare: '>=' },
        { kind: 'traderStanding', traderId: 't2', value: 3, compare: '>=' },
        after('A', 'Success'),
      ],
    }),
  ])
  const g = kappaGraph(live)!

  it('잠김 사유에 있는 조건만 미충족(현재 값), 없는 조건은 충족', () => {
    const p = progress({
      [COLLECTOR_ID]: qp('Locked', [
        { kind: 'level', need: 40, compare: '>=', current: 32 },
        { kind: 'traderLoyalty', traderId: 't1', need: 4, compare: '>=', current: 3 },
      ]),
    }, 32)
    const c = kappaConds(g, p, kappaDone(g, p))
    expect(c.level).toEqual({ need: 40, current: 32, met: false })
    expect(c.traders).toEqual([
      { traderId: 't1', kind: 'loyalty', need: 4, current: 3, met: false },
      { traderId: 't2', kind: 'loyalty', need: 4, current: null, met: true },
      { traderId: 't2', kind: 'standing', need: 3, current: null, met: true },
    ])
    expect(c.quests).toEqual({ done: 0, total: 1 })
  })

  it('바닐라형(퀘스트 조건만)은 레벨·상인 조건이 비어 있다', () => {
    const vg = kappaGraph(vanilla)!
    const c = kappaConds(vg, progress({}), new Set())
    expect(c.level).toBeNull()
    expect(c.traders).toEqual([])
  })
})

describe('kappaItems', () => {
  it('제출함 / FIR 보유 / FIR 아님 / 없음', () => {
    const cat = catalog([collector({ objectives: [handover('o1', 'i1'), handover('o2', 'i2'), handover('o3', 'i3'), handover('o4', 'i4')] })])
    const g = kappaGraph(cat)!
    const p = progress({ [COLLECTOR_ID]: qp('Started', [], { o1: { current: 1, target: 1, done: true } }) })
    const inv: Record<string, Holding> = { i1: { count: 1, fir: 1 }, i2: { count: 1, fir: 1 }, i3: { count: 2, fir: 0 } }
    expect(kappaItems(g, p, inv).map((i) => i.state)).toEqual(['given', 'ready', 'nonFir', 'none'])
  })
})

describe('chainStats · kappaLists', () => {
  const g = kappaGraph(vanilla)!

  it('뒤로 N개 = 닿는 미완료 카파 퀘스트, 연쇄 = 최장 길이', () => {
    const stats = chainStats(g, new Set())
    expect(stats.get('A')).toEqual({ after: 2, chain: 2 })
    expect(stats.get('C')).toEqual({ after: 0, chain: 0 })
    expect(chainStats(g, new Set(['A'])).has('A')).toBe(false)
  })

  it('지금 할 수 있는 것은 연쇄가 긴 순, 상인 필터', () => {
    const p = progress({ A: qp('AvailableForStart'), D: qp('Started'), B: qp('Locked'), C: qp('Locked') })
    const done = kappaDone(g, p)
    const lists = kappaLists(vanilla, g, p, done, chainStats(g, done))
    expect(lists.now.map((q) => q.id)).toEqual(['A', 'D'])
    expect(lists.locked.map((q) => q.id).sort()).toEqual(['B', 'C'])
    expect(kappaLists(vanilla, g, p, done, chainStats(g, done), 't2').now.map((q) => q.id)).toEqual(['D'])
  })

  it('재시작 가능 실패는 지금 할 수 있는 것에 들어간다', () => {
    const p = progress({ A: qp('FailRestartable') })
    const done = kappaDone(g, p)
    expect(kappaLists(vanilla, g, p, done, chainStats(g, done)).now.map((q) => q.id)).toContain('A')
  })
})

describe('traderRows', () => {
  it('상인별 카파 퀘스트 완료/전체 + 상인 조건, 상인 순서', () => {
    const g = kappaGraph(vanilla)!
    const rows = traderRows(vanilla, g, new Set(['A']), [{ traderId: 't2', kind: 'loyalty', need: 4, current: 3, met: false }])
    expect(rows.map((r) => [r.traderId, r.done, r.total, r.conds.length])).toEqual([['t1', 1, 3, 0], ['t2', 0, 1, 1]])
  })
})

describe('chainTiers', () => {
  it('위는 조상, 아래는 자손을 최장 거리 단계로', () => {
    const cat = catalog([
      quest('A'),
      quest('B', { requirements: [after('A', 'Success')] }),
      quest('M', { requirements: [after('A', 'Success'), after('B', 'Success')] }),
      quest('N', { requirements: [after('M', 'Success')] }),
      collector({ requirements: [after('N', 'Success')] }),
    ])
    const g = kappaGraph(cat)!
    expect(chainTiers(cat, g, 'M')).toEqual({ ups: [['B'], ['A']], downs: [['N']] })
    expect(chainTiers(cat, g, 'A')).toEqual({ ups: [], downs: [['B'], ['M'], ['N']] })
  })
})

describe('끝내면 열림 · 새 프로필', () => {
  const lockOn = (questId: string, currentStatus = 'Locked', ...needStatuses: string[]) =>
    ({ kind: 'quest' as const, questId, needStatuses: needStatuses.length ? needStatuses : ['Success'], currentStatus })
  // A 진행 중 → B(A 완료 필요) → C(B 완료 필요). D 는 레벨만, S 는 수락만 요구(이미 진행 중이면 사유 없음)
  const prog = progress({
    A: qp('Started'),
    B: qp('Locked', [lockOn('A', 'Started'), lockOn('S', 'AvailableForStart', 'Started')]),
    C: qp('Locked', [lockOn('B')]),
    D: qp('Locked', [{ kind: 'level', need: 30, compare: '>=', current: 10 }]),
  })

  it('kappaUnlocks: 못 채운 퀘스트 선행마다 그 선행에 막힌 카파 퀘스트를 모은다', () => {
    const g = kappaGraph(vanilla)!
    const u = kappaUnlocks(vanilla, g, prog, kappaDone(g, prog))
    expect(u.get('A')?.map((q) => q.id)).toEqual(['B'])
    expect(u.get('S')?.map((q) => q.id)).toEqual(['B'])
    expect(u.get('B')?.map((q) => q.id)).toEqual(['C'])
  })

  it('nothingOpen: 전부 Locked 일 때만', () => {
    expect(nothingOpen(progress({ A: qp('Locked'), B: qp('Locked') }))).toBe(true)
    expect(nothingOpen(prog)).toBe(false)
  })
})

describe('제출 아이템 출처', () => {
  const item = (...tpls: string[]) => ({ conditionId: 'c', items: tpls.map((tpl) => ({ tpl, name: tpl })), state: 'none' as const })
  const sources = {
    a: { containers: [{ tpl: 'safe', name: 'Safe', chance: 0.03 }, { tpl: 'bag', name: 'Bag', chance: 0.002 }], bots: ['pmc' as const] },
    b: { containers: [{ tpl: 'bag', name: 'Bag', chance: 0.01 }], bots: ['scav' as const] },
  }

  it('itemSource: 대체 tpl 은 컨테이너마다 큰 확률로 합치고 봇은 표시 순서로', () => {
    expect(itemSource(item('a'), sources)).toBe(sources.a)
    expect(itemSource(item('a', 'b'), sources)).toEqual({
      containers: [{ tpl: 'safe', name: 'Safe', chance: 0.03 }, { tpl: 'bag', name: 'Bag', chance: 0.01 }],
      bots: ['scav', 'pmc'],
    })
    expect(itemSource(item('x'), sources)).toBeNull()
    expect(itemSource(item('a'), undefined)).toBeNull()
  })

  it('formatChance', () => {
    expect(formatChance(0.0308)).toBe('3.1%')
    expect(formatChance(0.0023)).toBe('0.2%')
    expect(formatChance(0.0004)).toBe('<0.1%')
  })
})

describe('상인별 트리', () => {
  /** 트리 하나 만들기 — 카파 그래프는 Collector 가 ends 를 요구하는 것으로 만든다 */
  const build = (quests: CatalogQuest[], ends: string[], status: Record<string, string> = {}, collapse = false) => {
    const cat = catalog([...quests, collector({ requirements: ends.map((id) => after(id, 'Success')) })])
    const g = kappaGraph(cat)!
    const p = progress(Object.fromEntries(Object.entries(status).map(([id, s]) => [id, qp(s)])))
    const done = kappaDone(g, p)
    return { g, p, done, tree: kappaTree(cat, g, p, done, 't1', collapse) }
  }
  const node = (tree: KappaTree, id: string) => tree.nodes.find((n) => n.id === id)!
  const cols = (tree: KappaTree) => Object.fromEntries(tree.nodes.map((n) => [n.id, n.col]))
  const edges = (tree: KappaTree) => tree.edges.map((e) => `${e.from}>${e.to}`).sort()
  const edge = (tree: KappaTree, from: string, to: string) => tree.edges.find((e) => e.from === from && e.to === to)!

  it('treeState: 선행 없는 Locked → next, 선행이 now → next, 선행이 Locked → far, 택일 실패 → done', () => {
    const { g, p, done } = build([
      quest('P'),
      quest('Q', { requirements: [after('P', 'Success')] }),
      quest('R', { requirements: [after('Q', 'Success')] }),
      quest('F'),
      quest('G', { requirements: [after('F', 'Success', 'Fail')] }),
    ], ['R', 'G'], { P: 'Started', F: 'Fail' })
    expect(treeState(g, p, done, 'P')).toBe('now')
    expect(treeState(g, p, done, 'Q')).toBe('next')
    expect(treeState(g, p, done, 'R')).toBe('far')
    expect(treeState(g, p, done, 'F')).toBe('done')
    expect(treeState(g, p, done, 'G')).toBe('next')
    const fresh = build([quest('P'), quest('Q', { requirements: [after('P', 'Success')] })], ['Q'])
    expect(treeState(fresh.g, fresh.p, fresh.done, 'P')).toBe('next')
    expect(treeState(fresh.g, fresh.p, fresh.done, 'Q')).toBe('far')
  })

  // A → B → C 사슬, A → E·B → E(A→E 는 한 열 건너뜀), 다른 상인 O → F·O → C, O2 → C 만
  const wide = [
    quest('A'),
    quest('B', { requirements: [after('A', 'Success')] }),
    quest('C', { requirements: [after('B', 'Success'), after('O', 'Success'), after('O2', 'Success')] }),
    quest('E', { requirements: [after('A', 'Success'), after('B', 'Success')] }),
    quest('F', { requirements: [after('O', 'Success')] }),
    quest('O', { traderId: 't2' }),
    quest('O2', { traderId: 't2' }),
  ]

  it('열: 상인 안 최장 깊이, 바깥 선행만 있는 루트는 1열, 바깥 노드 = 가장 앞 자식 − 1', () => {
    const { tree } = build(wide, ['C', 'E', 'F'])
    expect(cols(tree)).toEqual({ A: 0, B: 1, C: 2, E: 2, F: 1, O: 0, O2: 1 })
    expect(node(tree, 'O').kind).toBe('outside')
    expect(node(tree, 'A').kind).toBe('quest')
    expect(tree).toMatchObject({ cols: 3, total: 5, doneCount: 0 })
  })

  it('두 열 건너뛰는 간선은 via 하나, 인접 간선은 []', () => {
    const { tree } = build(wide, ['C', 'E', 'F'])
    expect(edge(tree, 'A', 'E').via).toHaveLength(1)
    expect(edge(tree, 'O', 'C').via).toHaveLength(1)
    expect(edge(tree, 'A', 'B').via).toEqual([])
  })

  it('같은 열 안 노드는 row 가 1 이상 벌어진다', () => {
    const { tree } = build(wide, ['C', 'E', 'F'])
    for (let c = 0; c < tree.cols; c++) {
      const rows = tree.nodes.filter((n) => n.col === c).map((n) => n.row).sort((a, b) => a - b)
      for (let i = 1; i < rows.length; i++) expect(rows[i] - rows[i - 1]).toBeGreaterThanOrEqual(1)
    }
    expect(tree.rows).toBeGreaterThan(Math.max(...tree.nodes.map((n) => n.row)))
  })

  it('행: 이름순이면 교차하는 배치가 barycenter 후 교차 없음, 한 줄 사슬은 같은 row', () => {
    const { tree } = build([
      quest('A'), quest('B'),
      quest('X', { requirements: [after('B', 'Success')] }),
      quest('Y', { requirements: [after('A', 'Success')] }),
    ], ['X', 'Y'])
    expect(node(tree, 'Y').row).toBe(node(tree, 'A').row)
    expect(node(tree, 'X').row).toBe(node(tree, 'B').row)
    const chain = build([
      quest('A'),
      quest('B', { requirements: [after('A', 'Success')] }),
      quest('C', { requirements: [after('B', 'Success')] }),
    ], ['C'])
    expect(chain.tree.nodes.map((n) => n.row)).toEqual([0, 0, 0])
  })

  describe('완료 접기', () => {
    // A(완료) → B(진행 중) → C, 완료 바깥 O → B, 미완료 바깥 O2 → C
    const quests = [
      quest('A'),
      quest('B', { requirements: [after('A', 'Success'), after('O', 'Success')] }),
      quest('C', { requirements: [after('B', 'Success'), after('O2', 'Success')] }),
      quest('O', { traderId: 't2' }),
      quest('O2', { traderId: 't2' }),
    ]
    const status = { A: 'Success', O: 'Success', B: 'Started' }

    it('켜면 완료 상인 노드 대신 접힌 칸 하나 + 그 간선, 완료 바깥 노드는 빠지고 미완료 바깥 노드는 남는다', () => {
      const { tree } = build(quests, ['C'], status, true)
      expect(tree.nodes.map((n) => n.id).sort()).toEqual([TREE_DONE_BOX, 'B', 'C', 'O2'].sort())
      expect(edges(tree)).toEqual([`${TREE_DONE_BOX}>B`, 'B>C', 'O2>C'].sort())
      expect(node(tree, TREE_DONE_BOX)).toMatchObject({ kind: 'doneBox', state: 'done', col: 0 })
      expect(node(tree, 'B').col).toBe(1)
      expect(tree.doneCount).toBe(1)
    })

    it('끄면 접힌 칸 없이 전부', () => {
      const { tree } = build(quests, ['C'], status)
      expect(tree.nodes.map((n) => n.id).sort()).toEqual(['A', 'B', 'C', 'O', 'O2'])
      expect(node(tree, 'A').state).toBe('done')
    })

    it('모두 완료 + 접기 → 접힌 칸 하나뿐, 간선 0', () => {
      const { tree } = build(quests, ['C'], { A: 'Success', B: 'Success', C: 'Success', O: 'Success', O2: 'Success' }, true)
      expect(tree.nodes.map((n) => n.id)).toEqual([TREE_DONE_BOX])
      expect(tree.edges).toEqual([])
      expect(tree).toMatchObject({ doneCount: 3, total: 3, cols: 1, rows: 1 })
    })
  })

  it('soft: failOk 노드에서 나간 간선만', () => {
    const { tree } = build([
      quest('P'),
      quest('Q', { requirements: [after('P', 'Success', 'Fail')] }),
      quest('R', { requirements: [after('Q', 'Success')] }),
    ], ['R'])
    expect(edge(tree, 'P', 'Q').soft).toBe(true)
    expect(edge(tree, 'Q', 'R').soft).toBe(false)
  })

  it('treeFocus: 조상·자손 포함, 형제 제외', () => {
    const { tree } = build([
      quest('A'),
      quest('B', { requirements: [after('A', 'Success')] }),
      quest('C', { requirements: [after('A', 'Success')] }),
      quest('D', { requirements: [after('B', 'Success')] }),
    ], ['C', 'D'])
    expect([...treeFocus(tree, 'B')].sort()).toEqual(['A', 'B', 'D'])
  })

  it('edgePath: 인접 간선은 C 하나, via 하나면 C 둘 + H 하나', () => {
    const { tree } = build(wide, ['C', 'E', 'F'])
    const ops = (from: string, to: string) => edgePath(edge(tree, from, to), tree).replace(/[^MCH]/g, '')
    expect(ops('A', 'B')).toBe('MC')
    expect(ops('A', 'E')).toBe('MCHC')
    // 한 줄 사슬: A 오른쪽 가운데(10+196, 10+20) → B 왼쪽 가운데(10+252, 같은 높이)
    const chain = build([quest('A'), quest('B', { requirements: [after('A', 'Success')] })], ['B']).tree
    expect(edgePath(edge(chain, 'A', 'B'), chain)).toBe('M206 30 C234 30 234 30 262 30')
  })
})
