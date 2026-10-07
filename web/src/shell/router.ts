import { useEffect, useState } from 'react'

export type Page = 'wiki' | 'progress'
/** 진행현황의 소메뉴. 사이드 메뉴 트리 순서와 같다. */
export type ProgressSub = 'overview' | 'raid' | 'items' | 'unlocks' | 'kappa'

export interface Route {
  page: Page
  /** page 가 progress 일 때만 값이 있다 */
  sub: ProgressSub | null
  query: URLSearchParams
}

export const PROGRESS_SUBS: readonly ProgressSub[] = ['overview', 'raid', 'items', 'unlocks', 'kappa']
const DEFAULT_ROUTE: Route = { page: 'wiki', sub: null, query: new URLSearchParams() }

/**
 * '#/wiki?quest=x' → { page: 'wiki', query }, '#/progress/raid?map=woods' → { page: 'progress', sub: 'raid', query }.
 * 진행현황은 소메뉴가 없거나 모르는 값이면 overview. 그 밖에 모르는 값은 null (호출자가 기본 라우트로 치환).
 */
export function parseHash(hash: string): Route | null {
  const raw = hash.startsWith('#') ? hash.slice(1) : hash
  const q = raw.indexOf('?')
  const path = q === -1 ? raw : raw.slice(0, q)
  const query = new URLSearchParams(q === -1 ? '' : raw.slice(q + 1))
  if (!path.startsWith('/')) return null
  const [page, sub, ...rest] = path.slice(1).split('/')
  if (page === 'wiki' && sub === undefined) return { page, sub: null, query }
  if (page === 'progress' && rest.length === 0) {
    const known = (PROGRESS_SUBS as readonly string[]).includes(sub ?? '')
    return { page, sub: known ? (sub as ProgressSub) : 'overview', query }
  }
  return null
}

export function hashFor(page: Page, sub?: ProgressSub | null, query?: Record<string, string>): string {
  const path = page === 'progress' && sub ? `#/${page}/${sub}` : `#/${page}`
  const qs = query ? new URLSearchParams(query).toString() : ''
  return qs === '' ? path : `${path}?${qs}`
}

/** 메뉴 클릭. hashchange 가 발화해 useHashRoute 가 갱신된다. <a href> 는 Blazor 가 가로챌 수 있어 쓰지 않는다 (§4.6). */
export function navigate(page: Page, sub?: ProgressSub | null, query?: Record<string, string>): void {
  location.hash = hashFor(page, sub, query)
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
