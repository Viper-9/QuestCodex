import { describe, expect, it } from 'vitest'
import { loadLang, saveLang } from './lang'

function fakeStorage(init: Record<string, string> = {}) {
  const map = new Map(Object.entries(init))
  return {
    getItem: (k: string) => map.get(k) ?? null,
    setItem: (k: string, v: string) => { map.set(k, v) },
    map,
  }
}

describe('loadLang', () => {
  it('저장값이 목록 안이면 그대로', () => {
    expect(loadLang(fakeStorage({ 'questcodex.lang': 'kr' }), 'en-US')).toBe('kr')
  })
  it('저장값이 없고 브라우저가 ko 로 시작하면 kr (게임 로케일 키)', () => {
    expect(loadLang(fakeStorage(), 'ko-KR')).toBe('kr')
  })
  it('저장값이 없고 브라우저가 ko 가 아니면 en', () => {
    expect(loadLang(fakeStorage(), 'de-DE')).toBe('en')
  })
  it('저장값이 목록 밖(ko — 서버 UI 로케일 키)이면 무시하고 브라우저 언어로', () => {
    expect(loadLang(fakeStorage({ 'questcodex.lang': 'ko' }), 'ko')).toBe('kr')
  })
  it('storage 가 null 이면 브라우저 언어로', () => {
    expect(loadLang(null, 'ko')).toBe('kr')
  })
})

describe('saveLang', () => {
  it('questcodex.lang 키에 저장', () => {
    const s = fakeStorage()
    saveLang('en', s)
    expect(s.map.get('questcodex.lang')).toBe('en')
  })
  it('storage 가 null 이어도 예외 없음', () => {
    expect(() => saveLang('kr', null)).not.toThrow()
  })
})
