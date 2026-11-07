import { describe, expect, it } from 'vitest'
import en from '../i18n/en.json'
import type { T } from '../i18n/index'
import { bossLine } from './MapMarkers'
import type { TabBossEntry } from './mapProjection'

const t = ((key: string, params?: Record<string, unknown>) =>
  Object.entries(params ?? {}).reduce(
    (text, [name, value]) => text.replace(`{${name}}`, String(value)),
    (en as Record<string, string>)[key] ?? key,
  )) as T

const boss = (over: Partial<TabBossEntry> = {}): TabBossEntry =>
  ({ key: 'bossBully', kind: 'boss', chance: 10, mapChance: 30, conditional: false, rotating: false, ...over })

describe('bossLine', () => {
  it('맵 전체 확률 하나만 쓴다', () => {
    expect(bossLine(boss(), t)).toBe('Reshala 30%')
  })

  it('광신도 사제는 밤 표시를 붙이고, 야간 공장에서는 생략한다', () => {
    const priest = boss({ key: 'sectantPriest' })
    expect(bossLine(priest, t)).toBe('Cultist Priest (night) 30%')
    expect(bossLine({ ...priest, onlyOn: 'sandbox_high' }, t)).toBe('Cultist Priest (high level, night) 30%')
    expect(bossLine({ ...priest, onlyOn: 'factory4_night' }, t)).toBe('Cultist Priest (night) 30%')
  })

  it('맵을 순환하는 보스는 순환 표시와 확률을 함께 쓴다', () => {
    expect(bossLine(boss({ key: 'bossKnight', rotating: true, mapChance: 35 }), t)).toBe('Goons (rotating) 35%')
  })

  it('조건부 표시는 유지한다', () => {
    expect(bossLine(boss({ conditional: true }), t)).toBe('Reshala 30% · needs a trigger')
  })
})
