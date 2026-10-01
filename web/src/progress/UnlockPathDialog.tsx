import { useEffect, useRef } from 'react'
import type { Catalog } from '../api/catalog'
import type { ProfileProgress } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import type { NameLookup } from '../wiki/derive'
import { formatInt } from '../wiki/format'
import { useDialogFrame } from '../wiki/useDialogFrame'
import { QuestLink } from './parts'
import { canDoNow, type SourcePlan, type UnlockRow } from './unlock'
import { pathTiers } from './unlockLines'
import { questNameOf, sourceLabel } from './unlockText'

interface UnlockPathDialogProps {
  row: UnlockRow | null
  /** null 이면 닫힘. 항상 마운트해 두고 showModal()/close() 로 토글한다(위키 설명 팝업과 같은 틀). */
  plan: SourcePlan | null
  catalog: Catalog
  progress: ProfileProgress
  lookup: NameLookup
  onClose(): void
}

/** 전체 경로 팝업 — 단계(depth)별 한 줄, 같은 줄은 동시에 진행 가능. 목표가 마지막 줄(★). 스크롤은 팝업 본문 안에서만. */
export function UnlockPathDialog({ row, plan, catalog, progress, lookup, onClose }: UnlockPathDialogProps) {
  const t = useT()
  const ref = useRef<HTMLDialogElement>(null)
  const open = row !== null && plan !== null
  const frame = useDialogFrame(ref, 'unlockPath', open, onClose)

  useEffect(() => {
    const el = ref.current
    if (!el) return
    if (open && !el.open) el.showModal()
    else if (!open && el.open) el.close()
  }, [open])

  const tiers = plan ? pathTiers(plan.steps) : []
  const source = plan ? sourceLabel(plan.source, lookup, t) : ''
  const levelShort = plan?.maxLevel != null && plan.maxLevel > progress.level

  return (
    <dialog ref={ref} className="qc-dialog qc-udialog" onClose={onClose} {...frame.dialogProps}>
      {frame.grips}
      {row && plan && (
        <header className="qc-dialog__head" {...frame.headProps}>
          <button type="button" className="qc-dialog__close" aria-label={t('dialog.close')} onClick={onClose}>✕</button>
          <h3 className="qc-dialog__title">{t('unlock.dialog.title', { quest: questNameOf(catalog, plan.source.questId, t) })}</h3>
          <p className="qc-dialog__meta">{t('unlock.dialog.meta', { item: row.name, source, profile: progress.nickname, level: progress.level })}</p>
        </header>
      )}
      {row && plan && (
        <div className="qc-dialog__body">
          <p className="qc-udialog__sum">
            {t('unlock.dialog.sum', { n: formatInt(plan.remaining), tiers: tiers.length })}
            {plan.maxLevel !== null && <> · <span className={levelShort ? 'qc-warn' : undefined}>
              {levelShort ? t('unlock.sum.levelShort', { n: plan.maxLevel, now: progress.level }) : `${t('unlock.sum.maxLevel', { n: plan.maxLevel })} ✓`}
            </span></>}
            {' · '}<span className="qc-muted">{t('unlock.dialog.parallel')}</span>
          </p>
          <ol className="qc-utiers">
            {tiers.map((tier, i) => {
              const goal = tier.some((s) => s.questId === plan.source.questId)
              const nowTier = tier.some(canDoNow)
              return (
                <li key={i} className={cls('qc-utier', nowTier && 'is-now', goal && 'is-goal')}>
                  <span className="qc-utier__n">{goal ? '★' : i + 1}</span>
                  <span className="qc-utier__qs">
                    {tier.map((s) => {
                      const q = catalog.quests[s.questId]
                      const lv = q?.minLevel ?? null
                      return (
                        <span key={s.questId} className={cls('qc-utier__q', canDoNow(s) && 'is-now')}>
                          {q ? <QuestLink quest={q} /> : t('unlock.unknownQuest')}
                          {q && <span className="qc-muted"> {lookup.traderName(q.traderId)}</span>}
                          {lv !== null && lv > 1 && <span className={cls('qc-utier__lv', lv > progress.level && 'qc-warn')}> {t('unlock.lv', { n: lv })}</span>}
                          {s.failsOnComplete.length > 0 && <span className="qc-warn" title={t('unlock.step.fails', {
                            quests: s.failsOnComplete.map((id) => questNameOf(catalog, id, t)).join(', '),
                          })}> ⚠</span>}
                        </span>
                      )
                    })}
                  </span>
                  <span className="qc-utier__tail">
                    {goal ? t('unlock.dialog.goal', { source }) : nowTier ? t('unlock.dialog.nowTier') : ''}
                  </span>
                </li>
              )
            })}
          </ol>
        </div>
      )}
    </dialog>
  )
}
