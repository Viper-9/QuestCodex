import { useCatalog } from './shell/useCatalog'
import { useTheme, type ThemePref } from './shell/theme'
import { navigate, useHashRoute } from './shell/router'
import { SideMenu } from './shell/SideMenu'
import { TopBar } from './shell/TopBar'
import { ErrorBanner } from './shell/ErrorBanner'
import { I18nProvider } from './i18n/I18nContext'
import { ProgressPage } from './progress/ProgressPage'
import { WikiPage } from './wiki/WikiPage'

interface AppProps {
  initialTheme: ThemePref
}

export function App({ initialTheme }: AppProps) {
  const theme = useTheme(initialTheme)
  const route = useHashRoute()
  const c = useCatalog()

  return (
    <I18nProvider lang={c.lang}>
      <div className="qc-shell" data-theme={theme.resolved}>
        <SideMenu page={route.page} onNavigate={navigate} />
        <div className="qc-main">
          <TopBar
            theme={theme.pref} onThemeChange={theme.setPref}
            lang={c.lang} onLangChange={c.setLang}
            busy={c.loading && c.catalog !== null}
          />
          <div className="qc-page">
            {route.page === 'progress' && <ProgressPage />}
            {route.page === 'wiki' && (
              <>
                {c.error && <ErrorBanner code={c.error} onRetry={c.retry} />}
                {(c.catalog || !c.error) && <WikiPage catalog={c.catalog} route={route} />}
              </>
            )}
          </div>
        </div>
      </div>
    </I18nProvider>
  )
}
