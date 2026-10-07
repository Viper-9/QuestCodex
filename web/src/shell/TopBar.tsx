import { useT } from '../i18n/I18nContext'
import { LANGS, LANG_LABELS, type Lang } from './lang'
import { THEME_PREFS, type ThemePref } from './theme'

interface TopBarProps {
  /** 사이드 메뉴가 펼쳐져 있나 — 접혀 있으면 메뉴 버튼 옆에 로고를 대신 보여 준다 */
  sideOpen: boolean
  onToggleSide(): void
  onHome(): void
  theme: ThemePref
  onThemeChange(theme: ThemePref): void
  lang: Lang
  onLangChange(lang: Lang): void
  /** 카탈로그가 이미 있는데 재요청 중일 때 작은 표시 (§3.1) */
  busy: boolean
  /** 데이터 갱신 — 누르는 즉시 App 이 전체 화면 덮개를 띄우고 서버 캐시를 다시 만든다 (13 catalog-cache 스펙 §4) */
  onRebuild(): void
  /** 카탈로그를 받는 중이면 누를 수 없다 */
  rebuildDisabled: boolean
}

export function TopBar({ sideOpen, onToggleSide, onHome, theme, onThemeChange, lang, onLangChange, busy, onRebuild, rebuildDisabled }: TopBarProps) {
  const t = useT()
  const menuLabel = t(sideOpen ? 'topbar.menuHide' : 'topbar.menuShow')
  return (
    <header className="qc-topbar">
      <button type="button" className="qc-topbar__menu" aria-expanded={sideOpen} aria-label={menuLabel} title={menuLabel} onClick={onToggleSide}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true">
          <path d="M4 6h16M4 12h16M4 18h16" />
        </svg>
      </button>
      {!sideOpen && <button type="button" className="qc-topbar__logo" onClick={onHome}>QuestCodex</button>}
      <div className="qc-topbar__spacer" />
      {busy && <span className="qc-topbar__busy" aria-live="polite">{t('topbar.busy')}</span>}
      <button type="button" className="qc-topbar__rebuild" title={t('rebuild.hint')} disabled={rebuildDisabled} onClick={onRebuild}>
        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M21 12a9 9 0 1 1-2.64-6.36L21 8" />
          <path d="M21 3v5h-5" />
        </svg>
        {t('rebuild.button')}
      </button>
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
