import { useT } from '../i18n/I18nContext'
import { LANGS, LANG_LABELS, type Lang } from './lang'
import { THEME_PREFS, type ThemePref } from './theme'

interface TopBarProps {
  theme: ThemePref
  onThemeChange(theme: ThemePref): void
  lang: Lang
  onLangChange(lang: Lang): void
  /** 카탈로그가 이미 있는데 재요청 중일 때 작은 표시 (§3.1) */
  busy: boolean
}

export function TopBar({ theme, onThemeChange, lang, onLangChange, busy }: TopBarProps) {
  const t = useT()
  return (
    <header className="qc-topbar">
      <div className="qc-topbar__spacer" />
      {busy && <span className="qc-topbar__busy" aria-live="polite">{t('topbar.busy')}</span>}
      <select className="qc-select" aria-label={t('topbar.theme')} value={theme} onChange={(e) => onThemeChange(e.target.value as ThemePref)}>
        {/* `theme.${p}` 는 템플릿 리터럴 타입이라 UiKey 로 좁혀진다 — 사전에 키가 없으면 컴파일 오류 */}
        {THEME_PREFS.map((p) => <option key={p} value={p}>{t(`theme.${p}`)}</option>)}
      </select>
      {/* 언어 이름은 번역하지 않는다 (각 언어의 자기 표기) */}
      <select className="qc-select" aria-label={t('topbar.lang')} value={lang} onChange={(e) => onLangChange(e.target.value as Lang)}>
        {LANGS.map((l) => <option key={l} value={l}>{LANG_LABELS[l]}</option>)}
      </select>
    </header>
  )
}
