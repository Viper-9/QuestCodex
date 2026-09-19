import type { Catalog } from './api/catalog'
import { useCatalog } from './shell/useCatalog'
import { useTheme, type ThemePref } from './shell/theme'
import { navigate, useHashRoute } from './shell/router'
import { SideMenu } from './shell/SideMenu'
import { TopBar } from './shell/TopBar'
import { ErrorBanner } from './shell/ErrorBanner'
import { I18nProvider, useT } from './i18n/I18nContext'
import { ProgressPage } from './progress/ProgressPage'

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
            {route.page === 'wiki' && <CatalogStatus loading={c.loading} error={c.error} catalog={c.catalog} onRetry={c.retry} />}
          </div>
        </div>
      </div>
    </I18nProvider>
  )
}

/** Task 1·2 의 임시 표시 (Task 4 에서 WikiPage 로 교체) */
function CatalogStatus({ loading, error, catalog, onRetry }: { loading: boolean; error: string | null; catalog: Catalog | null; onRetry(): void }) {
  const t = useT()
  return (
    <>
      {loading && !catalog && <p className="qc-placeholder">{t('app.loading')}</p>}
      {error && <ErrorBanner code={error} onRetry={onRetry} />}
      {catalog && <p className="qc-placeholder">quests={Object.keys(catalog.quests).length} traders={Object.keys(catalog.traders).length} lang={catalog.lang}</p>}
    </>
  )
}
