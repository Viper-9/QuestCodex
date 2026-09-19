import { createContext, useContext, useMemo, type ReactNode } from 'react'
import type { Lang } from '../shell/lang'
import { getT, type T } from './index'

const I18nContext = createContext<T>(getT('en'))

/** App 이 현재 언어로 한 번 감싼다. 언어가 바뀌면 t 가 바뀌고 useT() 를 쓰는 컴포넌트가 전부 다시 그려진다. */
export function I18nProvider({ lang, children }: { lang: Lang; children: ReactNode }) {
  const t = useMemo(() => getT(lang), [lang])
  return <I18nContext.Provider value={t}>{children}</I18nContext.Provider>
}

/** 컴포넌트 안에서 UI 문자열 조회: const t = useT(); t('menu.wiki') */
export function useT(): T {
  return useContext(I18nContext)
}
