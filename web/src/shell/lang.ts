import { safeStorage } from './storage'

// 서버에 지원 언어 목록 엔드포인트가 없어 하드코딩 (스펙 §0).
// 언어 코드는 EFT 게임 텍스트 로케일(database/locales/global) 의 키를 따른다 — 한국어는 ISO 'ko' 가 아니라 'kr'.
// 'ko' 는 SPT 서버 UI 로케일 키라서 카탈로그 요청에 쓰면 400 이다 (2단계 스펙 §2 실측 정정, 2026-09-19).
export const LANGS = ['en', 'kr'] as const
export type Lang = (typeof LANGS)[number]
export const LANG_LABELS: Record<Lang, string> = { en: 'English', kr: '한국어' }

const KEY = 'questcodex.lang'

type ReadStorage = Pick<Storage, 'getItem'>
type WriteStorage = Pick<Storage, 'setItem'>

export function isLang(x: unknown): x is Lang {
  return typeof x === 'string' && (LANGS as readonly string[]).includes(x)
}

/** 저장값 → 없거나 목록 밖이면 navigator.language(ISO, 'ko-KR' 등) 가 ko 로 시작할 때 kr, 아니면 en (§1.1). */
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
  return navLang.toLowerCase().startsWith('ko') ? 'kr' : 'en'
}

export function saveLang(lang: Lang, storage: WriteStorage | null = safeStorage()): void {
  try {
    storage?.setItem(KEY, lang)
  } catch {
    // 저장 실패(용량·프라이빗 모드)는 무시 — 다음 방문에 기본값으로 돌아갈 뿐
  }
}
