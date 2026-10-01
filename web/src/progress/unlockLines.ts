import type { Catalog } from '../api/catalog'
import type { PathStep } from './unlock'

// 해금 경로의 표시용 줄(스펙 §1.4). 펼친 행의 카드는 pathLines, 전체 경로 팝업은 pathTiers 를 쓴다.

export type PathLine =
  | { kind: 'quest'; step: PathStep }
  | { kind: 'series'; steps: PathStep[]; label: string }
  | { kind: 'gap'; hidden: number }

/** 카드에 그대로 보여 주는 최대 줄 수. 넘으면 앞 2 + gap + 뒤 2. */
const MAX_LINES = 5
const KEEP = 2

/** 연작 접미어 — "Part 3", "파트 3", "3부", "Часть 3". 앞의 구분자(" - " 등)는 이름에서 뗀다. */
const SERIES = /^(.*?)[\s\-–—:]*(?:part\s*(\d+)|파트\s*(\d+)|(\d+)\s*부|часть\s*(\d+))$/i

export function seriesOf(name: string): { base: string; n: number } | null {
  const m = SERIES.exec(name.trim())
  if (!m || !m[1]) return null
  return { base: m[1], n: Number(m[2] ?? m[3] ?? m[4] ?? m[5]) }
}

const questCount = (l: PathLine) => (l.kind === 'quest' ? 1 : l.kind === 'series' ? l.steps.length : l.hidden)

export function pathLines(steps: PathStep[], catalog: Catalog): PathLine[] {
  const goal = steps.at(-1)
  const keyOf = (s: PathStep) => {
    if (s === goal) return null // 목표는 따로 한 줄 — 연작에 묻히면 목표 표시가 사라진다
    const q = catalog.quests[s.questId]
    const series = q && seriesOf(q.name)
    return series ? `${series.base}|${q.traderId}` : null
  }
  const groups = new Map<string, PathStep[]>()
  for (const s of steps) {
    const key = keyOf(s)
    if (key) groups.set(key, [...(groups.get(key) ?? []), s])
  }

  const lines: PathLine[] = []
  const emitted = new Set<string>()
  for (const s of steps) {
    const key = keyOf(s)
    const members = key ? groups.get(key)! : []
    if (!key || members.length < 2) { lines.push({ kind: 'quest', step: s }); continue }
    if (emitted.has(key)) continue
    emitted.add(key)
    const last = Math.max(...members.map((m) => seriesOf(catalog.quests[m.questId].name)!.n))
    lines.push({ kind: 'series', steps: members, label: `${catalog.quests[members[0].questId].name} ~ ${last}` })
  }

  if (lines.length <= MAX_LINES) return lines
  const hidden = lines.slice(KEEP, -KEEP).reduce((n, l) => n + questCount(l), 0)
  return [...lines.slice(0, KEEP), { kind: 'gap', hidden }, ...lines.slice(-KEEP)]
}

/** 팝업의 단계 목록 — 같은 depth 를 한 줄로(같은 줄 = 동시에 진행 가능). steps 의 순서를 유지한다. */
export function pathTiers(steps: PathStep[]): PathStep[][] {
  const tiers: PathStep[][] = []
  for (const s of steps) (tiers[s.depth] ??= []).push(s)
  return tiers.filter(Boolean)
}
