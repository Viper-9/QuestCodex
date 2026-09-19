import { safeStorage } from './storage'

// 서버에 지원 언어 목록 엔드포인트가 없어 하드코딩 (스펙 §0). 한국어 키는 'kr' 이 아니라 'ko'.
export const LANGS = ['en', 'ko'] as const
export type Lang = (typeof LANGS)[number]
export const LANG_LABELS: Record<Lang, string> = { en: 'English', ko: '한국어' }

const KEY = 'questcodex.lang'

type ReadStorage = Pick<Storage, 'getItem'>
type WriteStorage = Pick<Storage, 'setItem'>

export function isLang(x: unknown): x is Lang {
  return typeof x === 'string' && (LANGS as readonly string[]).includes(x)
}

/** 저장값 → 없거나 목록 밖이면 navigator.language 가 ko 로 시작할 때 ko, 아니면 en (§1.1). */
export function loadLang(
  storage: ReadStorage | null = safeStorage(),
  navLang: string = typeof navigator === 'undefined' ? 'en' : navigator.language,
): Lang {
  try {
    const saved = storage?.getItem(KEY)
    if (isLang(saved)) return saved
  } catch {
    // 읽기 실패는 저장값 없음과 같게 취급
  }
  return navLang.toLowerCase().startsWith('ko') ? 'ko' : 'en'
}

export function saveLang(lang: Lang, storage: WriteStorage | null = safeStorage()): void {
  try {
    storage?.setItem(KEY, lang)
  } catch {
    // 저장 실패(용량·프라이빗 모드)는 무시 — 다음 방문에 기본값으로 돌아갈 뿐
  }
}
