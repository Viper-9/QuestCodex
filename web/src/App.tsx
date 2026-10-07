import { useState } from 'react'
import { useCatalog } from './shell/useCatalog'
import { safeStorage } from './shell/storage'
import { useTheme, type ThemePref } from './shell/theme'
import { navigate, useHashRoute } from './shell/router'
import { SideMenu } from './shell/SideMenu'
import { TopBar } from './shell/TopBar'
import { ErrorBanner } from './shell/ErrorBanner'
import { PreparingNotice, RebuildOverlay } from './shell/PreparingNotice'
import { I18nProvider } from './i18n/I18nContext'
import { ProgressPage } from './progress/ProgressPage'
import { WikiPage } from './wiki/WikiPage'

/** 사이드 메뉴 펼침 여부 — localStorage 에 저장(막힌 환경이면 펼침으로 시작하고 저장만 건너뛴다) */
const SIDE_KEY = 'qc.side.open'

function useSideOpen(): [boolean, () => void] {
  const [open, setOpen] = useState(() => safeStorage()?.getItem(SIDE_KEY) !== '0')
  const toggle = () => {
    const next = !open
    setOpen(next)
    try { safeStorage()?.setItem(SIDE_KEY, next ? '1' : '0') } catch { /* 저장 못 해도 이번 세션 동작엔 지장 없음 */ }
  }
  return [open, toggle]
}

interface AppProps {
  initialTheme: ThemePref
}

export function App({ initialTheme }: AppProps) {
  const theme = useTheme(initialTheme)
  const route = useHashRoute()
  const c = useCatalog()
  const [sideOpen, toggleSide] = useSideOpen()

  return (
    <I18nProvider lang={c.lang}>
      <div className="qc-shell" data-theme={theme.resolved}>
        {sideOpen && <SideMenu route={route} onNavigate={navigate} />}
        <div className="qc-main">
          <TopBar
            sideOpen={sideOpen} onToggleSide={toggleSide} onHome={() => navigate('wiki')}
            theme={theme.pref} onThemeChange={theme.setPref}
            lang={c.lang} onLangChange={c.setLang}
            busy={c.loading && c.catalog !== null && !c.rebuilding}
            onRebuild={c.rebuild} rebuildDisabled={c.loading}
          />
          <div className="qc-page">
            {c.preparing && <PreparingNotice />}
            {c.error && <ErrorBanner code={c.error} onRetry={c.retry} />}
            {route.page === 'progress' && <ProgressPage catalog={c.catalog} route={route} />}
            {route.page === 'wiki' && (c.catalog || !c.error) && <WikiPage catalog={c.catalog} route={route} />}
          </div>
        </div>
        {c.rebuilding && <RebuildOverlay />}
      </div>
    </I18nProvider>
  )
}
