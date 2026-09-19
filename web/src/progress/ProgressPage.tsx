import { useT } from '../i18n/I18nContext'

/** 후속 스펙(questcodex-frontend-progress)에서 교체. */
export function ProgressPage() {
  const t = useT()
  return <p className="qc-placeholder">{t('progress.placeholder')}</p>
}
