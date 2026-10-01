import { describe, expect, it } from 'vitest'
import type { Catalog, CatalogQuest } from '../api/catalog'
import type { PathStep } from './unlock'
import { pathLines, pathTiers } from './unlockLines'

function catalog(names: Record<string, string>, trader: Record<string, string> = {}): Catalog {
  const quests: Record<string, CatalogQuest> = {}
  for (const [id, name] of Object.entries(names)) {
    quests[id] = {
      id, name, description: '', traderId: trader[id] ?? 'lotus', side: 'Pmc', factionOnly: null, isVanilla: true, modName: null, imageUrl: null,
      minLevel: null, location: null, locationKey: null, requirements: [], prerequisites: [], unlocks: [], failsWhen: [], objectives: [],
      rewards: { started: [], success: [], fail: [] }, tags: [],
    }
  }
  return { sptVersion: '', modVersion: '', generatedAt: '', lang: 'en', traders: {}, rewardIndex: {}, warnings: [], itemCategories: [], itemCategoryOf: {}, quests }
}

const step = (questId: string, depth: number): PathStep => ({ questId, mode: 'complete', depth, status: 'Locked', failsOnComplete: [] })
const shape = (lines: ReturnType<typeof pathLines>) =>
  lines.map((l) => (l.kind === 'quest' ? l.step.questId : l.kind === 'series' ? `${l.label} (${l.steps.length})` : `gap ${l.hidden}`))

describe('pathLines', () => {
  const names = {
    new: 'New Contact', l1: 'Light of Lotus - Part 1', l2: 'Light of Lotus - Part 2', l3: 'Light of Lotus - Part 3',
    l4: 'Light of Lotus - Part 4', l5: 'Light of Lotus - Part 5', bear: 'Bear Hunter',
  }
  const all = ['new', 'l1', 'l2', 'l3', 'l4', 'l5', 'bear'].map(step)
  // 목표는 마지막 단계 — sourcePath 가 목표를 항상 끝에 둔다

  it('10a. 연작 Part 1~5 를 첫 멤버 자리에 한 줄로', () => {
    expect(shape(pathLines(all, catalog(names)))).toEqual(['new', 'Light of Lotus - Part 1 ~ 5 (5)', 'bear'])
  })

  it('10b. 일부를 끝냈으면 남은 범위만', () => {
    expect(shape(pathLines([all[0], all[3], all[4], all[5], all[6]], catalog(names)))).toEqual(['new', 'Light of Lotus - Part 3 ~ 5 (3)', 'bear'])
  })

  it('목표(마지막 단계)는 연작에 합치지 않는다', () => {
    const cat = catalog({ a: 'Prog - Part 1', b: 'Prog - Part 2', c: 'Prog - Part 3' })
    expect(shape(pathLines(['a', 'b', 'c'].map(step), cat))).toEqual(['Prog - Part 1 ~ 2 (2)', 'c'])
    expect(shape(pathLines(['a', 'b'].map(step), cat))).toEqual(['a', 'b'])
  })

  it('10c. 5줄 이하는 그대로, 상인이 다르면 연작이 아니다', () => {
    const cat = catalog({ a: 'Chem - Part 1', b: 'Chem - Part 2', c: 'C', d: 'D', e: 'E' }, { b: 'other' })
    expect(shape(pathLines(['a', 'b', 'c', 'd', 'e'].map(step), cat))).toEqual(['a', 'b', 'c', 'd', 'e'])
  })

  it('10d. 7줄 → 앞 2 + gap(3) + 뒤 2, 숨긴 수는 퀘스트 수', () => {
    const cat = catalog({ a: 'A', b: 'B', s1: 'Saw 1부', s2: 'Saw 2부', c: 'C', d: 'D', e: 'E', f: 'F' })
    expect(shape(pathLines(['a', 'b', 's1', 's2', 'c', 'd', 'e', 'f'].map(step), cat))).toEqual(['a', 'b', 'gap 4', 'e', 'f'])
  })
})

describe('pathTiers', () => {
  it('depth 별로 묶는다', () => {
    const tiers = pathTiers([step('r1', 0), step('r2', 0), step('m', 1), step('t', 2)])
    expect(tiers.map((t) => t.map((s) => s.questId))).toEqual([['r1', 'r2'], ['m'], ['t']])
  })
})
