import { useEffect, useMemo, useRef, useState } from 'react'
import type { Catalog } from '../api/catalog'
import type { ProfileProgress, QuestStatus } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { navigate } from '../shell/router'
import type { NameLookup } from '../wiki/derive'
import { useDialogFrame } from '../wiki/useDialogFrame'
import { questProgress } from './derive'
import { chainTiers, type KappaGraph } from './kappa'
import type { PathStep } from './unlock'
import { pathFocus } from './unlockLines'

/** 아래쪽(남은 후속)을 처음에 보여 주는 단계 수. 넘으면 "N단계 더 보기" */
const DOWN_TIERS = 6
const NOW = new Set<QuestStatus>(['AvailableForFinish', 'Started', 'AvailableForStart', 'FailRestartable'])

interface KappaChainDialogProps {
  /** null 이면 닫힘. 항상 마운트해 두고 showModal()/close() 로 토글한다(해금 경로 팝업과 같은 틀) */
  questId: string | null
  catalog: Catalog
  graph: KappaGraph
  progress: ProfileProgress
  done: ReadonlySet<string>
  lookup: NameLookup
  onClose(): void
}

/**
 * 카파 연쇄 전체 보기(스펙 §2.7) — 가운데 ★ = 고른 퀘스트, 위 = 지나온 카파 퀘스트(완료분은 한 줄로 접힘), 아래 = 남은 카파 후속.
 * 단계 줄·칩·걸러 보기는 해금 경로 팝업의 모양(.qc-utier*)과 pathFocus 를 그대로 쓴다.
 */
export function KappaChainDialog({ questId, catalog, graph, progress, done, lookup, onClose }: KappaChainDialogProps) {
  const t = useT()
  const ref = useRef<HTMLDialogElement>(null)
  const open = questId !== null
  const frame = useDialogFrame(ref, 'kappaChain', open, onClose)
  // 다른 퀘스트로 열면 접힘·더 보기·걸러 보기 초기화 — 상태에 퀘스트 id 를 붙여 두고 다르면 무시한다
  const [pastOpen, setPastOpen] = useState<string | null>(null)
  const [moreOpen, setMoreOpen] = useState<string | null>(null)
  const [focused, setFocused] = useState<{ key: string; id: string } | null>(null)

  useEffect(() => {
    const el = ref.current
    if (!el) return
    if (open && !el.open) el.showModal()
    else if (!open && el.open) el.close()
  }, [open])

  const tiers = useMemo(() => (questId ? chainTiers(catalog, graph, questId) : null), [catalog, graph, questId])
  const steps = useMemo((): PathStep[] => {
    if (!questId || !tiers) return []
    return [questId, ...tiers.ups.flat(), ...tiers.downs.flat()]
      .map((id) => ({ questId: id, mode: 'complete', depth: 0, status: questProgress(progress, id).status, failsOnComplete: [] }))
  }, [questId, tiers, progress])
  const focusId = focused && focused.key === questId ? focused.id : null
  const focus = useMemo(() => (focusId ? pathFocus(steps, catalog, focusId) : null), [steps, catalog, focusId])

  if (!questId || !tiers) return <dialog ref={ref} className="qc-dialog qc-udialog" onClose={onClose} {...frame.dialogProps} />

  const quest = catalog.quests[questId]
  const showPast = pastOpen === questId
  const pastDone = tiers.ups.flat().filter((id) => done.has(id)).length
  // 위쪽은 먼 단계부터(−N … −1). 접혀 있으면 완료분을 빼고 남은 선행만
  const ups = tiers.ups.map((ids, i) => ({ n: -(i + 1), ids: showPast ? ids : ids.filter((id) => !done.has(id)) })).reverse()
  const downs = tiers.downs.map((ids, i) => ({ n: i + 1, ids }))
  const hiddenDowns = moreOpen === questId ? 0 : Math.max(0, downs.length - DOWN_TIERS)

  const chip = (id: string) => {
    const q = catalog.quests[id]
    const state = done.has(id) ? 'is-done' : NOW.has(questProgress(progress, id).status) ? 'is-now' : null
    return (
      <button key={id} type="button" aria-pressed={id === focusId} onClick={() => setFocused(id === focusId ? null : { key: questId, id })}
        className={cls('qc-utier__q', state, id === focusId && 'is-focus')}>
        <span className="qc-utier__name">{done.has(id) ? '✓ ' : ''}{q?.name ?? id}</span>
      </button>
    )
  }
  const tier = (n: number | '★', all: string[], goal = false) => {
    const ids = focus ? all.filter((id) => focus.shown.has(id)) : all
    if (ids.length === 0) return null
    const groups = new Map<string, string[]>()
    for (const id of ids) {
      const trader = catalog.quests[id]?.traderId ?? ''
      groups.set(trader, [...(groups.get(trader) ?? []), id])
    }
    return (
      <li key={String(n)} className={cls('qc-utier', ids.some((id) => !done.has(id) && NOW.has(questProgress(progress, id).status)) && 'is-now', goal && 'is-goal')}>
        <span className="qc-utier__n">{typeof n === 'number' && n > 0 ? `+${n}` : n}</span>
        <span className="qc-utier__groups">
          {[...groups].map(([trader, group]) => (
            <span key={trader} className="qc-utier__group">
              <span className="qc-utier__trader">{lookup.traderName(trader)}</span>
              <span className="qc-utier__qs">{group.map(chip)}</span>
            </span>
          ))}
        </span>
        <span className="qc-utier__tail" />
      </li>
    )
  }

  return (
    <dialog ref={ref} className="qc-dialog qc-udialog" onClose={onClose} {...frame.dialogProps}>
      {frame.grips}
      <header className="qc-dialog__head" {...frame.headProps}>
        <button type="button" className="qc-dialog__close" aria-label={t('dialog.close')} onClick={onClose}>✕</button>
        <h3 className="qc-dialog__title">{t('kappa.chain.title', { quest: quest?.name ?? questId })}</h3>
        <p className="qc-dialog__meta">{t('kappa.chain.meta', { profile: progress.nickname, level: progress.level })}</p>
      </header>
      <div className="qc-dialog__body">
        <p className="qc-udialog__sum">
          <span className="qc-udialog__tip">💡 {t('unlock.dialog.focusHint')}</span>
        </p>
        {focusId && (
          <p className="qc-udialog__focus">
            <span>{t('unlock.dialog.focus', { quest: catalog.quests[focusId]?.name ?? focusId })}</span>
            <button type="button" className="qc-link" onClick={() => navigate('wiki', null, { quest: focusId })}>{t('unlock.dialog.openWiki')}</button>
            <button type="button" className="qc-link" onClick={() => setFocused(null)}>{t('unlock.dialog.showAll')} ✕</button>
          </p>
        )}
        {pastDone > 0 && (
          <button type="button" className={cls('qc-kappa__past', showPast && 'is-open')} aria-expanded={showPast}
            onClick={() => setPastOpen(showPast ? null : questId)}>
            {showPast ? '▾' : '▸'} {t('kappa.chain.past', { n: pastDone })}
            <span className="qc-muted">{' — '}{t(showPast ? 'kappa.chain.pastHide' : 'kappa.chain.pastHint')}</span>
          </button>
        )}
        <ol className="qc-utiers">
          {ups.map((x) => tier(x.n, x.ids))}
          {tier('★', [questId], true)}
        </ol>
        <h4 className="qc-kappa__chainh">{t('kappa.chain.after')}</h4>
        {downs.length === 0 ? <p className="qc-muted">{t('kappa.chain.none')}</p> : (
          <ol className="qc-utiers">
            {downs.slice(0, downs.length - hiddenDowns).map((x) => tier(x.n, x.ids))}
          </ol>
        )}
        {hiddenDowns > 0 && (
          <button type="button" className="qc-link qc-kappa__more" onClick={() => setMoreOpen(questId)}>{t('kappa.chain.more', { n: hiddenDowns })}</button>
        )}
      </div>
    </dialog>
  )
}
