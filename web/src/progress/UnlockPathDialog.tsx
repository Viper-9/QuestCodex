import { useEffect, useMemo, useRef, useState } from 'react'
import type { Catalog } from '../api/catalog'
import type { ProfileProgress } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import type { NameLookup } from '../wiki/derive'
import { formatInt } from '../wiki/format'
import { navigate } from '../shell/router'
import { useDialogFrame } from '../wiki/useDialogFrame'
import { canDoNow, type PathStep, type SourcePlan, type UnlockRow } from './unlock'
import { pathFocus, pathTiers } from './unlockLines'
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

/** 전체 경로 팝업 — 단계(depth)별 한 줄, 목표가 마지막 줄(★). 칩을 누르면 그 퀘스트와 연결된 퀘스트만 남긴다(위키는 걸러 보기 줄에서). 스크롤은 팝업 본문 안에서만. */
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

  // 칩을 누르면 그 퀘스트의 선행·후행만 남긴다. 다른 경로를 열면 초기화. 고른 퀘스트가 경로에서 빠지면(완료) 전체 보기로.
  const planKey = row && plan ? `${row.tpl}|${plan.source.questId}|${plan.source.kind}` : ''
  const [focused, setFocused] = useState<{ key: string; questId: string } | null>(null)
  const focusId = focused && focused.key === planKey && plan?.steps.some((s) => s.questId === focused.questId) ? focused.questId : null
  const focus = useMemo(() => (plan && focusId ? pathFocus(plan.steps, catalog, focusId) : null), [plan, catalog, focusId])
  const toggleFocus = (questId: string) => setFocused(focusId === questId ? null : { key: planKey, questId })

  const byTrader = (tier: PathStep[]) => {
    const groups = new Map<string, PathStep[]>()
    for (const s of tier) {
      const id = catalog.quests[s.questId]?.traderId ?? ''
      groups.set(id, [...(groups.get(id) ?? []), s])
    }
    return [...groups]
  }
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
          <div className="qc-udialog__sum">
            <span>
              {t('unlock.sum.remaining', { n: formatInt(plan.remaining) })}
              {plan.maxLevel !== null && <> · <span className={levelShort ? 'qc-warn' : undefined}>
                {levelShort ? t('unlock.sum.levelShort', { n: plan.maxLevel, now: progress.level }) : `${t('unlock.sum.maxLevel', { n: plan.maxLevel })} ✓`}
              </span></>}
            </span>
            {/* 팁은 한 단계에 퀘스트가 2개 이상일 때만(일직선이면 걸러 볼 일이 없다). 걸러 보는 중에도 유지 */}
            {tiers.some((tier) => tier.length > 1) && <span className="qc-udialog__tip">💡 {t('unlock.dialog.focusHint')}</span>}
          </div>
          {focusId && (
            <p className="qc-udialog__focus">
              <span>{t('unlock.dialog.focus', { quest: questNameOf(catalog, focusId, t) })}</span>
              <button type="button" className="qc-link" onClick={() => navigate('wiki', null, { quest: focusId })}>{t('unlock.dialog.openWiki')}</button>
              <button type="button" className="qc-link" onClick={() => setFocused(null)}>{t('unlock.dialog.showAll')} ✕</button>
            </p>
          )}
          <ol className="qc-utiers">
            {tiers.map((all, i) => {
              const tier = focus ? all.filter((s) => focus.shown.has(s.questId)) : all
              if (tier.length === 0) return null // 단계 번호는 원래대로 둔다
              const goal = tier.some((s) => s.questId === plan.source.questId)
              return (
                <li key={i} className={cls('qc-utier', tier.some(canDoNow) && 'is-now', goal && 'is-goal')}>
                  <span className="qc-utier__n">{goal ? '★' : i + 1}</span>
                  {/* 같은 단계 안에서 상인별로 한 줄 — 상인 이름은 줄 앞에 한 번만(steps 가 이미 상인 순) */}
                  <span className="qc-utier__groups">
                    {byTrader(tier).map(([traderId, group]) => (
                      <span key={traderId} className="qc-utier__group">
                        <span className="qc-utier__trader" title={traderId ? lookup.traderName(traderId) : undefined}>{traderId ? lookup.traderName(traderId) : ''}</span>
                        <span className="qc-utier__qs">
                          {group.map((s) => {
                            const q = catalog.quests[s.questId]
                            const lv = q?.minLevel ?? null
                            const hidden = focus?.hiddenPrereqs.get(s.questId) ?? 0
                            return (
                              <button key={s.questId} type="button" aria-pressed={s.questId === focusId} onClick={() => toggleFocus(s.questId)}
                                className={cls('qc-utier__q', s.questId === focusId && 'is-focus')}>
                                <span className="qc-utier__name">{q ? q.name : t('unlock.unknownQuest')}</span>
                                {lv !== null && lv > 1 && <span className={cls('qc-utier__lv', lv > progress.level && 'qc-warn')}>{t('unlock.lv', { n: lv })}</span>}
                                {s.failsOnComplete.length > 0 && <span className="qc-warn" title={t('unlock.step.fails', {
                                  quests: s.failsOnComplete.map((id) => questNameOf(catalog, id, t)).join(', '),
                                })}>⚠</span>}
                                {hidden > 0 && <span className="qc-utier__more" title={t('unlock.dialog.otherPrereqsHint')}>{t('unlock.dialog.otherPrereqs', { n: hidden })}</span>}
                              </button>
                            )
                          })}
                        </span>
                      </span>
                    ))}
                  </span>
                  <span className="qc-utier__tail">
                    {goal && t('unlock.dialog.goal', { source })}
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
