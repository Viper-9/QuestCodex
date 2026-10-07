import { useT } from '../i18n/I18nContext'

/** 첫 로드에서 서버가 looseLoot·staticLoot 를 읽는 동안(첫 실행·모드 변경 후) 띄우는 안내 띠 (13 catalog-cache 스펙 §5) */
export function PreparingNotice() {
  const t = useT()
  return (
    <div className="qc-notice" role="status" aria-live="polite">
      <span className="qc-notice__spinner" aria-hidden="true" />
      <span>{t('notice.preparing')}</span>
    </div>
  )
}

/** "데이터 다시 만들기" 중 전체 화면을 어둡게 덮어 조작을 막고 진행 중임을 알린다 (13 catalog-cache 스펙 §4) */
export function RebuildOverlay() {
  const t = useT()
  return (
    <div className="qc-overlay" role="alertdialog" aria-modal="true" aria-busy="true" aria-label={t('rebuild.progress')}>
      <div className="qc-overlay__box">
        <span className="qc-notice__spinner qc-overlay__spinner" aria-hidden="true" />
        <span>{t('rebuild.progress')}</span>
      </div>
    </div>
  )
}
