import { describe, expect, it } from 'vitest'
import { loadTheme, resolveTheme, saveTheme } from './theme'

function fakeStorage(init: Record<string, string> = {}) {
  const map = new Map(Object.entries(init))
  return { getItem: (k: string) => map.get(k) ?? null, setItem: (k: string, v: string) => { map.set(k, v) }, map }
}

describe('loadTheme', () => {
  it('저장값 light/dark/system 은 그대로', () => {
    expect(loadTheme(fakeStorage({ 'questcodex.theme': 'light' }))).toBe('light')
    expect(loadTheme(fakeStorage({ 'questcodex.theme': 'dark' }))).toBe('dark')
    expect(loadTheme(fakeStorage({ 'questcodex.theme': 'system' }))).toBe('system')
  })
  it('없거나 이상한 값이면 system', () => {
    expect(loadTheme(fakeStorage())).toBe('system')
    expect(loadTheme(fakeStorage({ 'questcodex.theme': 'blue' }))).toBe('system')
    expect(loadTheme(null)).toBe('system')
  })
})

describe('saveTheme', () => {
  it('questcodex.theme 키에 저장', () => {
    const s = fakeStorage()
    saveTheme('dark', s)
    expect(s.map.get('questcodex.theme')).toBe('dark')
  })
})

describe('resolveTheme', () => {
  it('system 은 OS 판정을 따른다', () => {
    expect(resolveTheme('system', true)).toBe('light')
    expect(resolveTheme('system', false)).toBe('dark')
  })
  it('light/dark 는 OS 와 무관', () => {
    expect(resolveTheme('light', false)).toBe('light')
    expect(resolveTheme('dark', true)).toBe('dark')
  })
})
