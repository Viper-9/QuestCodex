import { afterEach, describe, expect, it, vi } from 'vitest'
import { CatalogError, fetchCatalog } from './catalog'

afterEach(() => vi.unstubAllGlobals())

describe('fetchCatalog', () => {
  it('lang 을 쿼리로 보내고 JSON 을 그대로 돌려준다', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ lang: 'ko', traders: {}, quests: {} }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const c = await fetchCatalog('ko')
    expect(c.lang).toBe('ko')
    expect(fetchMock).toHaveBeenCalledWith('/questcodex/api/catalog?lang=ko', expect.objectContaining({}))
  })

  it('4xx/5xx 는 본문 error 코드를 담은 CatalogError', async () => {
    vi.stubGlobal('fetch', async () => new Response(JSON.stringify({ error: 'unknownLang', supported: ['en', 'ko'] }), { status: 400 }))
    await expect(fetchCatalog('kr')).rejects.toMatchObject({ code: 'unknownLang', status: 400 })
  })

  it('본문이 JSON 이 아니면 code 는 http<status>', async () => {
    vi.stubGlobal('fetch', async () => new Response('<html>', { status: 502 }))
    await expect(fetchCatalog('en')).rejects.toMatchObject({ code: 'http502', status: 502 })
  })

  it('네트워크 실패는 code network, status null', async () => {
    vi.stubGlobal('fetch', async () => { throw new TypeError('Failed to fetch') })
    const err = await fetchCatalog('en').catch((e: unknown) => e)
    expect(err).toBeInstanceOf(CatalogError)
    expect(err).toMatchObject({ code: 'network', status: null })
  })

  it('abort 는 CatalogError 로 감싸지 않고 그대로 던진다', async () => {
    vi.stubGlobal('fetch', async (_u: string, init?: RequestInit) => {
      throw init?.signal?.reason ?? new DOMException('aborted', 'AbortError')
    })
    const ctrl = new AbortController()
    ctrl.abort()
    const err = await fetchCatalog('en', ctrl.signal).catch((e: unknown) => e)
    expect(err).not.toBeInstanceOf(CatalogError)
  })
})
