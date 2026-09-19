import { useEffect, useState } from 'react'

export type Page = 'wiki' | 'progress'

export interface Route {
  page: Page
  query: URLSearchParams
}

const PAGES: readonly Page[] = ['wiki', 'progress']
const DEFAULT_ROUTE: Route = { page: 'wiki', query: new URLSearchParams() }

/** '#/wiki?quest=x' → { page: 'wiki', query }. 모르는 값은 null (호출자가 기본 라우트로 치환). */
export function parseHash(hash: string): Route | null {
  const raw = hash.startsWith('#') ? hash.slice(1) : hash
  const q = raw.indexOf('?')
  const path = q === -1 ? raw : raw.slice(0, q)
  const query = new URLSearchParams(q === -1 ? '' : raw.slice(q + 1))
  const page = path.startsWith('/') ? path.slice(1) : ''
  return (PAGES as readonly string[]).includes(page) ? { page: page as Page, query } : null
}

export function hashFor(page: Page): string {
  return `#/${page}`
}

/** 메뉴 클릭. hashchange 가 발화해 useHashRoute 가 갱신된다. <a href> 는 Blazor 가 가로챌 수 있어 쓰지 않는다 (§4.6). */
export function navigate(page: Page): void {
  location.hash = hashFor(page)
}

/** hashchange 없이 URL 만 바꾼다 — 딥링크 소비 후 ?quest 제거(§4.5), 잘못된 해시 정정. */
export function replaceHash(hash: string): void {
  history.replaceState(null, '', hash)
}

function readRoute(): Route {
  const r = parseHash(location.hash)
  if (r) return r
  replaceHash(hashFor(DEFAULT_ROUTE.page))
  return DEFAULT_ROUTE
}

export function useHashRoute(): Route {
  const [route, setRoute] = useState<Route>(readRoute)
  useEffect(() => {
    const onChange = () => setRoute(readRoute())
    window.addEventListener('hashchange', onChange)
    return () => window.removeEventListener('hashchange', onChange)
  }, [])
  return route
}
