import { useCallback, useEffect, useState } from 'react'
import { CatalogError, fetchCatalog, rebuildCatalog, type Catalog } from '../api/catalog'
import { loadLang, saveLang, type Lang } from './lang'

/** 이보다 오래 걸리면 "서버에서 준비 중" 안내를 띄운다 — 캐시가 맞으면 그 전에 끝나서 깜빡이지 않는다 */
const SLOW_MS = 1000

export interface CatalogState {
  /** 마지막으로 성공한 응답. 언어 전환 중에도 유지해 화면 깜빡임을 막는다 (§3.1). */
  catalog: Catalog | null
  loading: boolean
  /** 에러 코드: 응답 body.error / http<status> / network. 없으면 null. */
  error: string | null
}

/** 언어 + 카탈로그 로딩 상태. 마운트 시 1회, 언어 변경·재시도·다시 만들기 때 재요청. */
export function useCatalog() {
  const [lang, setLangState] = useState<Lang>(() => loadLang())
  const [attempt, setAttempt] = useState(0)
  const [state, setState] = useState<CatalogState>({ catalog: null, loading: true, error: null })
  const [slow, setSlow] = useState(false)
  const [rebuilding, setRebuilding] = useState(false)

  useEffect(() => {
    const ctrl = new AbortController()
    setState((s) => ({ ...s, loading: true, error: null }))
    setSlow(false)
    const timer = setTimeout(() => setSlow(true), SLOW_MS)
    const done = () => {
      clearTimeout(timer)
      setSlow(false)
      setRebuilding(false)
    }
    fetchCatalog(lang, ctrl.signal)
      .then((catalog) => {
        if (ctrl.signal.aborted) return
        done()
        setState({ catalog, loading: false, error: null })
      })
      .catch((err: unknown) => {
        if (ctrl.signal.aborted) return
        done()
        setState((s) => ({ ...s, loading: false, error: err instanceof CatalogError ? err.code : 'network' }))
      })
    return () => {
      clearTimeout(timer)
      ctrl.abort()
    }
  }, [lang, attempt])

  const setLang = useCallback((next: Lang) => {
    saveLang(next)
    setLangState(next)
  }, [])
  const retry = useCallback(() => setAttempt((n) => n + 1), [])

  /** 서버 캐시를 버리고 다시 받는다. 끝날 때까지 App 이 전체 화면 덮개(RebuildOverlay)를 띄운다. */
  const rebuild = useCallback(() => {
    setRebuilding(true)
    setState((s) => ({ ...s, loading: true, error: null }))
    rebuildCatalog()
      .then(() => setAttempt((n) => n + 1))
      .catch((err: unknown) => {
        setRebuilding(false)
        setState((s) => ({ ...s, loading: false, error: err instanceof CatalogError ? err.code : 'network' }))
      })
  }, [])

  /** 첫 로드가 오래 걸리는 중 — "서버에서 준비 중" 안내 띠. 다시 만들기는 덮개가 따로 맡는다 */
  const preparing = slow && state.loading && state.catalog === null && !rebuilding

  return { lang, setLang, catalog: state.catalog, loading: state.loading, error: state.error, retry, rebuild, rebuilding, preparing }
}
