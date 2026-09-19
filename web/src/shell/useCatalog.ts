import { useCallback, useEffect, useState } from 'react'
import { CatalogError, fetchCatalog, type Catalog } from '../api/catalog'
import { loadLang, saveLang, type Lang } from './lang'

export interface CatalogState {
  /** 마지막으로 성공한 응답. 언어 전환 중에도 유지해 화면 깜빡임을 막는다 (§3.1). */
  catalog: Catalog | null
  loading: boolean
  /** 에러 코드: 응답 body.error / http<status> / network. 없으면 null. */
  error: string | null
}

/** 언어 + 카탈로그 로딩 상태. 마운트 시 1회, 언어 변경·재시도 때 재요청. */
export function useCatalog() {
  const [lang, setLangState] = useState<Lang>(() => loadLang())
  const [attempt, setAttempt] = useState(0)
  const [state, setState] = useState<CatalogState>({ catalog: null, loading: true, error: null })

  useEffect(() => {
    const ctrl = new AbortController()
    setState((s) => ({ ...s, loading: true, error: null }))
    fetchCatalog(lang, ctrl.signal)
      .then((catalog) => {
        if (!ctrl.signal.aborted) setState({ catalog, loading: false, error: null })
      })
      .catch((err: unknown) => {
        if (ctrl.signal.aborted) return
        setState((s) => ({ ...s, loading: false, error: err instanceof CatalogError ? err.code : 'network' }))
      })
    return () => ctrl.abort()
  }, [lang, attempt])

  const setLang = useCallback((next: Lang) => {
    saveLang(next)
    setLangState(next)
  }, [])
  const retry = useCallback(() => setAttempt((n) => n + 1), [])

  return { lang, setLang, catalog: state.catalog, loading: state.loading, error: state.error, retry }
}
