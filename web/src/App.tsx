import type { Catalog } from './api/catalog'
import { useCatalog } from './shell/useCatalog'
import { ErrorBanner } from './shell/ErrorBanner'
import { LANGS, LANG_LABELS, type Lang } from './shell/lang'
import { I18nProvider, useT } from './i18n/I18nContext'

export function App() {
  const c = useCatalog()
  return (
    <I18nProvider lang={c.lang}>
      <div className="qc-shell">
        <select aria-label="lang" value={c.lang} onChange={(e) => c.setLang(e.target.value as Lang)}>
          {LANGS.map((l) => <option key={l} value={l}>{LANG_LABELS[l]}</option>)}
        </select>
        <CatalogStatus loading={c.loading} error={c.error} catalog={c.catalog} onRetry={c.retry} />
      </div>
    </I18nProvider>
  )
}

/** 임시 표시. useT() 는 I18nProvider 아래에서만 언어를 알므로 App 본문이 아니라 자식에서 부른다. Task 4 에서 WikiPage 로 교체. */
function CatalogStatus({ loading, error, catalog, onRetry }: { loading: boolean; error: string | null; catalog: Catalog | null; onRetry(): void }) {
  const t = useT()
  return (
    <>
      {loading && <p>{t('app.loading')}</p>}
      {error && <ErrorBanner code={error} onRetry={onRetry} />}
      {catalog && <p>quests={Object.keys(catalog.quests).length} traders={Object.keys(catalog.traders).length} lang={catalog.lang}</p>}
    </>
  )
}
