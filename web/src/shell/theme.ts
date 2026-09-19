import { useCallback, useEffect, useState } from 'react'
import { safeStorage } from './storage'

export type ThemePref = 'system' | 'light' | 'dark'
export type ResolvedTheme = 'light' | 'dark'

export const THEME_PREFS: readonly ThemePref[] = ['system', 'light', 'dark']
// 표시 라벨은 i18n/<lang>.json 의 theme.system / theme.light / theme.dark

const KEY = 'questcodex.theme'
const LIGHT_QUERY = '(prefers-color-scheme: light)'

type ReadStorage = Pick<Storage, 'getItem'>
type WriteStorage = Pick<Storage, 'setItem'>

export function isThemePref(x: unknown): x is ThemePref {
  return typeof x === 'string' && (THEME_PREFS as readonly string[]).includes(x)
}

export function loadTheme(storage: ReadStorage | null = safeStorage()): ThemePref {
  try {
    const v = storage?.getItem(KEY)
    return isThemePref(v) ? v : 'system'
  } catch {
    return 'system'
  }
}

export function saveTheme(pref: ThemePref, storage: WriteStorage | null = safeStorage()): void {
  try {
    storage?.setItem(KEY, pref)
  } catch {
    // 저장 실패는 무시
  }
}

export function resolveTheme(pref: ThemePref, systemIsLight: boolean): ResolvedTheme {
  if (pref === 'system') return systemIsLight ? 'light' : 'dark'
  return pref
}

export function querySystemIsLight(): boolean {
  return typeof matchMedia === 'function' && matchMedia(LIGHT_QUERY).matches
}

/** OS 테마 전환을 따라간다 (§1.3). 반환값을 호출하면 해제. */
export function subscribeSystemTheme(cb: (isLight: boolean) => void): () => void {
  if (typeof matchMedia !== 'function') return () => {}
  const mq = matchMedia(LIGHT_QUERY)
  const handler = (e: MediaQueryListEvent) => cb(e.matches)
  mq.addEventListener('change', handler)
  return () => mq.removeEventListener('change', handler)
}

/**
 * initial 은 main.tsx 가 렌더 전에 loadTheme() 로 읽어 넘긴다 — 첫 렌더부터 올바른 data-theme 이 붙어
 * 깜빡임이 없다 (§1.3).
 */
export function useTheme(initial: ThemePref) {
  const [pref, setPrefState] = useState<ThemePref>(initial)
  const [systemLight, setSystemLight] = useState<boolean>(() => querySystemIsLight())

  useEffect(() => subscribeSystemTheme(setSystemLight), [])

  const setPref = useCallback((next: ThemePref) => {
    saveTheme(next)
    setPrefState(next)
  }, [])

  return { pref, setPref, resolved: resolveTheme(pref, systemLight) }
}
