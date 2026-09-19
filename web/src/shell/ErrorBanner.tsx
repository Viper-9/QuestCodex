import { useT } from '../i18n/I18nContext'

interface ErrorBannerProps {
  code: string
  onRetry(): void
}

export function ErrorBanner({ code, onRetry }: ErrorBannerProps) {
  const t = useT()
  return (
    <div className="qc-error" role="alert">
      <span>{t('error.catalog', { code })}</span>
      <button type="button" className="qc-btn" onClick={onRetry}>{t('error.retry')}</button>
    </div>
  )
}
