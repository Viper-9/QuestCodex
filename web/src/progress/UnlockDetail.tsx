import type { Catalog, CatalogQuest } from '../api/catalog'
import type { ProfileProgress } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import type { NameLookup } from '../wiki/derive'
import { formatInt } from '../wiki/format'
import { isClosed } from './derive'
import { QuestLink } from './parts'
import { canDoNow, nextStep, type PathStep, type SourcePlan, type UnlockRow } from './unlock'
import { pathLines } from './unlockLines'
import { blockText, questNameOf, sourceLabel, stepNote } from './unlockText'

interface UnlockDetailProps {
  row: UnlockRow
  catalog: Catalog
  progress: ProfileProgress
  lookup: NameLookup
  modColors: Record<string, number>
  /** 펼친 입수처의 row.plans index */
  open: number
  onOpen(index: number): void
  onShowPath(index: number): void
}

/** 펼친 행: 이미 해금된 입수처 안내 줄 + 큰 카드 1장(펼친 입수처) + 접힌 작은 카드들. 접힌 카드를 누르면 그 카드가 펼쳐진다. */
export function UnlockDetail({ row, catalog, progress, lookup, modColors, open, onOpen, onShowPath }: UnlockDetailProps) {
  const t = useT()
  const done = row.plans.filter((p) => p.state === 'unlocked')
  const doneLine = (p: SourcePlan, key: 'unlock.done' | 'unlock.doneCraft') => (
    <p key={p.source.questId + p.source.kind} className="qc-udone">
      {t(key, { source: sourceLabel(p.source, lookup, t), quest: questNameOf(catalog, p.source.questId, t) })}
    </p>
  )
  if (row.state === 'unlocked') return <>{done.map((p) => doneLine(p, 'unlock.done'))}</>

  const cards = row.plans.map((plan, index) => ({ plan, index })).filter(({ plan }) => plan.state !== 'unlocked')
  const big = cards.find((c) => c.index === open) ?? cards[0]
  return (
    <>
      {row.craftUnlocked && done.filter((p) => p.source.kind === 'craft').slice(0, 1).map((p) => doneLine(p, 'unlock.doneCraft'))}
      <div className="qc-ucards">
        {cards.map(({ plan, index }) => index === big.index
          ? <PlanCard key={index} plan={plan} best={plan === row.best} catalog={catalog} progress={progress} lookup={lookup} modColors={modColors} onShowPath={() => onShowPath(index)} />
          : <PlanCompact key={index} plan={plan} catalog={catalog} progress={progress} lookup={lookup} modColors={modColors} onOpen={() => onOpen(index)} />)}
      </div>
    </>
  )
}

export function ModTag({ quest, colors }: { quest: CatalogQuest; colors: Record<string, number> }) {
  const t = useT()
  return (
    <span className="qc-tag qc-tag--mod" data-mod-color={quest.modName ? colors[quest.modName] : undefined} title={quest.modName ?? undefined}>
      {quest.modName ?? t('tag.mod')}
    </span>
  )
}

interface CardProps { plan: SourcePlan; catalog: Catalog; progress: ProfileProgress; lookup: NameLookup; modColors: Record<string, number> }

function CardHead({ plan, catalog, lookup, modColors }: Omit<CardProps, 'progress'>) {
  const t = useT()
  const quest = catalog.quests[plan.source.questId]
  return (
    <span className="qc-ucard__head">
      <span>{sourceLabel(plan.source, lookup, t)}{quest && !quest.isVanilla && <> <ModTag quest={quest} colors={modColors} /></>}</span>
      {plan.state === 'blocked'
        ? <span className="qc-tag qc-tag--bad">{t('unlock.chip.unreachable')}</span>
        : <span className="qc-ucard__n">{t('unlock.count', { n: formatInt(plan.remaining) })}</span>}
    </span>
  )
}

function PlanCard({ plan, best, catalog, progress, lookup, modColors, onShowPath }: CardProps & { best: boolean; onShowPath(): void }) {
  const t = useT()
  const now = nextStep(plan)
  const goal = plan.source.questId
  const on = plan.source.phase === 'started' ? 'unlock.card.onStart' : 'unlock.card.on'
  const levelOk = plan.maxLevel === null || plan.maxLevel <= progress.level
  const stepLine = (s: PathStep, label?: string, extra?: number) => (
    <>
      <span className="qc-usteps__name">
        {label ?? (catalog.quests[s.questId] ? <QuestLink quest={catalog.quests[s.questId]} /> : t('unlock.unknownQuest'))}
        {extra !== undefined && <span className="qc-usteps__cnt">{t('unlock.count', { n: extra })}</span>}
        {s.failsOnComplete.length > 0 && (
          <span className="qc-warn"> {t('unlock.step.fails', { quests: s.failsOnComplete.map((id) => questNameOf(catalog, id, t)).join(', ') })}</span>
        )}
      </span>
      <span className={s === now ? 'qc-usteps__now' : isClosed(s.status) ? 'qc-bad' : 'qc-muted'}>{stepNote(s, { now: s === now, goal: s.questId === goal }, t)}</span>
    </>
  )
  return (
    <div className={cls('qc-ucard', best && 'is-best')}>
      <CardHead plan={plan} catalog={catalog} lookup={lookup} modColors={modColors} />
      <p className="qc-ucard__meta">{t(on, { quest: questNameOf(catalog, goal, t) })}{best && ` · ${t('unlock.card.best')}`}</p>
      <ol className="qc-usteps">
        {pathLines(plan.steps, catalog).map((l, i) => {
          if (l.kind === 'gap') {
            return (
              <li key={i} className="is-gap">
                <button type="button" className="qc-link" onClick={onShowPath}>{t('unlock.gap', { n: l.hidden })}</button>
              </li>
            )
          }
          const s = l.kind === 'quest' ? l.step : l.steps[0]
          return (
            <li key={i} className={cls(l.kind === 'series' && 'is-series', canDoNow(s) && 'is-now', s.questId === goal && 'is-goal')}>
              {l.kind === 'quest' ? stepLine(s) : stepLine(s, l.label, l.steps.length)}
            </li>
          )
        })}
      </ol>
      <div className="qc-ucard__sum">
        {plan.blockReason ? <span className="qc-bad">{blockText(plan.blockReason, catalog, t)}</span> : (
          <>
            <span>{t('unlock.sum.remaining', { n: formatInt(plan.remaining) })}</span>
            {plan.maxLevel === null ? <span>{t('unlock.sum.noLevel')} ✓</span>
              : levelOk ? <span>{t('unlock.sum.maxLevel', { n: plan.maxLevel })} ✓</span>
              : <span className="qc-warn">{t('unlock.sum.levelShort', { n: plan.maxLevel, now: progress.level })}</span>}
            {plan.source.kind === 'sale' && <span>{t('unlock.sum.sale', { trader: lookup.traderName(plan.source.traderId ?? ''), n: plan.source.loyaltyLevel ?? 1 })}</span>}
            {plan.traderReqs.map((r) => (
              <span key={r.traderId + r.kind}>{t(r.kind === 'loyalty' ? 'unlock.sum.loyalty' : 'unlock.sum.standing', { trader: lookup.traderName(r.traderId), n: r.value })}</span>
            ))}
          </>
        )}
        <button type="button" className="qc-link qc-ucard__path" onClick={onShowPath}>{t('unlock.sum.path')}</button>
      </div>
    </div>
  )
}

function PlanCompact({ plan, catalog, progress, lookup, modColors, onOpen }: CardProps & { onOpen(): void }) {
  const t = useT()
  const now = nextStep(plan)
  return (
    <button type="button" className="qc-ucard qc-ucard--compact" onClick={onOpen}>
      <CardHead plan={plan} catalog={catalog} lookup={lookup} modColors={modColors} />
      <span className="qc-ucard__meta">
        {plan.blockReason ? blockText(plan.blockReason, catalog, t) : now ? questNameOf(catalog, now.questId, t) : questNameOf(catalog, plan.source.questId, t)}
        {plan.maxLevel !== null && plan.maxLevel > progress.level && <> · <span className="qc-warn">{t('unlock.levelNeed', { n: plan.maxLevel })}</span></>}
        {' · '}{t('unlock.card.expand')}
      </span>
    </button>
  )
}
