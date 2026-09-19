import { useT } from '../i18n/I18nContext'

/** 카탈로그 로딩 중: 상인 줄 자리 + 행 10개 회색 블록 (§3.1) */
export function WikiSkeleton() {
  const t = useT()
  return (
    <div className="qc-skeleton" aria-busy="true" aria-label={t('app.loading')}>
      <div className="qc-skeleton__strip" />
      {Array.from({ length: 10 }, (_, i) => <div key={i} className="qc-skeleton__row" />)}
    </div>
  )
}
