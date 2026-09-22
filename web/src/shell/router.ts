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

/**
 * 해시를 현재 경로 뒤에 붙인다. history.replaceState 의 URL 은 문서 base URL 기준으로 풀리는데,
 * 배포본 호스트 페이지(Blazor)는 <base href="/"> 를 달고 있어 '#/wiki' 만 넘기면 /questcodex 가
 * 통째로 날아가 '/#/wiki' 가 된다 (새로고침 → SPT 랜딩, 이후 해시 변경 → Blazor 가 '/' 를 로드).
 * vite dev 의 index.html 에는 <base> 가 없어 이 차이가 드러나지 않는다 (§4.6).
 */
export function urlWithHash(pathname: string, search: string, hash: string): string {
  return `${pathname}${search}${hash}`
}

/** hashchange 없이 URL 만 바꾼다 — 딥링크 소비 후 ?quest 제거(§4.5), 잘못된 해시 정정. */
export function replaceHash(hash: string): void {
  history.replaceState(null, '', urlWithHash(location.pathname, location.search, hash))
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
