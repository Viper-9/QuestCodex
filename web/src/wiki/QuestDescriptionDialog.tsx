import { useEffect, useRef } from 'react'
import type { CatalogQuest } from '../api/catalog'
import { useT } from '../i18n/I18nContext'
import { useDialogFrame } from './useDialogFrame'

interface QuestDescriptionDialogProps {
  /** null 이면 닫힘. 항상 마운트해 두고 showModal()/close() 로 토글한다. */
  quest: CatalogQuest | null
  traderName: string
  onClose(): void
}

/** (e) 설명 팝업. <dialog>.showModal() 이 ESC·포커스 트랩·백드롭을 제공한다 (§1.2 e). */
export function QuestDescriptionDialog({ quest, traderName, onClose }: QuestDescriptionDialogProps) {
  const t = useT()
  const ref = useRef<HTMLDialogElement>(null)
  const frame = useDialogFrame(ref, 'description', quest !== null, onClose)

  useEffect(() => {
    const el = ref.current
    if (!el) return
    if (quest && !el.open) el.showModal()
    else if (!quest && el.open) el.close()
  }, [quest])

  return (
    <dialog
      ref={ref}
      className="qc-dialog"
      onClose={onClose}
      {...frame.dialogProps}
    >
      {frame.grips}
      {quest && (
        <header className="qc-dialog__head" {...frame.headProps}>
          <button type="button" className="qc-dialog__close" aria-label={t('dialog.close')} onClick={onClose}>✕</button>
          <h3 className="qc-dialog__title">{quest.name}</h3>
          <p className="qc-dialog__meta">{traderName} · {t('dialog.level', { n: quest.minLevel ?? '—' })}</p>
        </header>
      )}
      {quest && (
        <div className="qc-dialog__body">
          <p className="qc-dialog__text">{quest.description}</p>
        </div>
      )}
    </dialog>
  )
}
