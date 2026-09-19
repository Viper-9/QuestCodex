import en from './en.json'
import ko from './ko.json'
import type { Lang } from '../shell/lang'

/** en.json 의 키가 곧 타입. 없는 키를 t() 에 넘기면 컴파일 오류. */
export type UiKey = keyof typeof en
export type T = (key: UiKey, params?: Record<string, string | number>) => string

// ko 를 Record<UiKey, string> 에 대입하므로 ko.json 에 키가 빠지면 tsc 가 잡는다 (i18n.test.ts 는 반대 방향도 검사).
const TABLES: Record<Lang, Record<UiKey, string>> = { en, ko }

/** "{n}개" 의 {n} 을 params.n 으로 치환. params 에 없는 자리표시자는 그대로 둔다. */
export function interpolate(template: string, params?: Record<string, string | number>): string {
  if (!params) return template
  return template.replace(/\{(\w+)\}/g, (m, k: string) => (k in params ? String(params[k]) : m))
}

export function getT(lang: Lang): T {
  const table = TABLES[lang]
  return (key, params) => interpolate(table[key], params)
}
