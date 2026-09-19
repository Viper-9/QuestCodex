import type { Catalog } from './api/catalog'
import { useCatalog } from './shell/useCatalog'
import { useTheme, type ThemePref } from './shell/theme'
import { TopBar } from './shell/TopBar'
import { ErrorBanner } from './shell/ErrorBanner'
import { I18nProvider, useT } from './i18n/I18nContext'

interface AppProps {
  initialTheme: ThemePref
}

export function App({ initialTheme }: AppProps) {
  const theme = useTheme(initialTheme)
  const c = useCatalog()

  return (
    <I18nProvider lang={c.lang}>
      <div className="qc-shell" data-theme={theme.resolved}>
        <div className="qc-main">
          <TopBar
            theme={theme.pref} onThemeChange={theme.setPref}
            lang={c.lang} onLangChange={c.setLang}
            busy={c.loading && c.catalog !== null}
          />
          <div className="qc-page">
            <CatalogStatus loading={c.loading} error={c.error} catalog={c.catalog} onRetry={c.retry} />
          </div>
        </div>
      </div>
    </I18nProvider>
  )
}

/** Task 1 의 임시 표시 그대로 (Task 4 에서 WikiPage 로 교체) */
function CatalogStatus({ loading, error, catalog, onRetry }: { loading: boolean; error: string | null; catalog: Catalog | null; onRetry(): void }) {
  const t = useT()
  return (
    <>
      {loading && !catalog && <p className="qc-muted" style={{ padding: 16 }}>{t('app.loading')}</p>}
      {error && <ErrorBanner code={error} onRetry={onRetry} />}
      {catalog && <p style={{ padding: 16 }}>quests={Object.keys(catalog.quests).length} traders={Object.keys(catalog.traders).length} lang={catalog.lang}</p>}
    </>
  )
}
