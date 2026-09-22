import { describe, expect, it } from 'vitest'
import en from './en.json'
import kr from './kr.json'
import { getT, interpolate } from './index'

const placeholders = (s: string) => (s.match(/\{\w+\}/g) ?? []).sort()

describe('사전 파일 en.json / kr.json', () => {
  it('키 집합이 같다', () => {
    expect(Object.keys(kr).sort()).toEqual(Object.keys(en).sort())
  })
  it('빈 값이 없다', () => {
    for (const table of [en, kr]) {
      for (const [k, v] of Object.entries(table)) expect(v, k).not.toBe('')
    }
  })
  it('자리표시자({n} 등) 집합이 언어 간 같다', () => {
    for (const k of Object.keys(en) as (keyof typeof en)[]) {
      expect(placeholders(kr[k]), k).toEqual(placeholders(en[k]))
    }
  })
})

describe('getT', () => {
  it('언어별 문자열', () => {
    expect(getT('kr')('menu.wiki')).toBe('위키')
    expect(getT('en')('menu.wiki')).toBe('Wiki')
  })
  it('자리표시자 치환', () => {
    expect(getT('kr')('filter.count', { n: 612 })).toBe('퀘스트 612개')
    expect(getT('en')('error.catalog', { code: 'network' })).toBe('Failed to load the catalog (network)')
  })
  it('interpolate 는 params 에 없는 자리표시자를 그대로 둔다', () => {
    expect(interpolate('{a}-{b}', { a: 1 })).toBe('1-{b}')
    expect(interpolate('plain')).toBe('plain')
  })
})
