import type { QuestStatus } from '../api/progress'
import { cls } from '../cls'
import type { T } from '../i18n/index'
import { useT } from '../i18n/I18nContext'
import type { NameLookup } from '../wiki/derive'
import { formatInt } from '../wiki/format'
import { itemDone, type KappaConds, type KappaItem, type TraderCond, type TraderRow } from './kappa'

// 카파 트래커 상단 — 왼쪽 "남은 것" + 종류별 막대, 오른쪽 상인 표. 합산 % 는 일부러 없다(스펙 §0: 가중치를 설명할 수 없어서).

interface SummaryProps {
  goalStatus: QuestStatus
  conds: KappaConds
  items: KappaItem[]
}

export function KappaSummary({ goalStatus, conds, items }: SummaryProps) {
  const t = useT()
  const itemsDone = items.filter(itemDone).length
  const tradersMet = conds.traders.filter((c) => c.met).length
  const parts = [
    conds.level && !conds.level.met ? t('kappa.left.level', { n: conds.level.need - conds.level.current }) : null,
    tradersMet < conds.traders.length ? t('kappa.left.traders', { n: new Set(conds.traders.filter((c) => !c.met).map((c) => c.traderId)).size }) : null,
    conds.quests.done < conds.quests.total ? t('kappa.left.quests', { n: formatInt(conds.quests.total - conds.quests.done) }) : null,
    itemsDone < items.length ? t('kappa.left.items', { n: items.length - itemsDone }) : null,
  ].filter((x): x is string => x !== null)

  let head: string
  if (goalStatus === 'Success') head = t('kappa.goal.done')
  else if (goalStatus === 'Started' || goalStatus === 'AvailableForFinish') head = t('kappa.goal.started')
  else if (goalStatus === 'AvailableForStart' || parts.length === 0) head = t('kappa.goal.ready')
  else head = t('kappa.left', { parts: parts.join(' · ') })

  return (
    <section className="qc-card qc-kappa__sum">
      <p className="qc-kappa__head">{head}</p>
      <ul className="qc-kappa__bars">
        {conds.level && <Bar label={t('kappa.bar.level')} done={conds.level.met ? conds.level.need : Math.min(conds.level.current, conds.level.need)} total={conds.level.need} />}
        {conds.traders.length > 0 && <Bar label={t('kappa.bar.traders')} done={tradersMet} total={conds.traders.length} />}
        <Bar label={t('kappa.bar.quests')} done={conds.quests.done} total={conds.quests.total} />
        {items.length > 0 && <Bar label={t('kappa.bar.items')} done={itemsDone} total={items.length} />}
      </ul>
    </section>
  )
}

function Bar({ label, done, total }: { label: string; done: number; total: number }) {
  const full = total > 0 && done >= total
  return (
    <li className={cls('qc-kappa__bar', full && 'is-full')}>
      <span className="qc-kappa__barhead">
        <span>{label}</span>
        <span className="qc-kappa__barnum">{full ? '✓ ' : ''}{formatInt(done)} / {formatInt(total)}</span>
      </span>
      <span className="qc-bar__track"><span className="qc-bar__fill" style={{ width: `${total > 0 ? (done / total) * 100 : 0}%` }} /></span>
    </li>
  )
}

/** 평판은 소수 둘째 자리까지, 끝의 0 은 뗀다 */
const num = (n: number) => String(Number(n.toFixed(2)))

function condText(c: TraderCond, t: T): string {
  if (c.met) return c.kind === 'loyalty' ? `${t('kappa.cond.ll', { need: c.need })} ✓` : `${t('kappa.cond.standing', { need: num(c.need) })} ✓`
  const current = c.current === null ? '?' : num(c.current)
  return c.kind === 'loyalty' ? t('kappa.cond.llShort', { current, need: c.need }) : t('kappa.cond.standingShort', { current, need: num(c.need) })
}

interface TradersProps {
  rows: TraderRow[]
  lookup: NameLookup
  selected: string | null
  onSelect(traderId: string): void
}

/** 상인 표 — 행을 누르면 아래 목록이 그 상인으로 걸러진다. 필요 등급 칸은 Collector 에 상인 조건이 있을 때만(sptQuestLive 형) */
export function KappaTraders({ rows, lookup, selected, onSelect }: TradersProps) {
  const t = useT()
  const hasConds = rows.some((r) => r.conds.length > 0)
  return (
    <section className={cls('qc-card qc-kappa__traders', hasConds && 'has-conds')} title={t('kappa.table.hint')}>
      <div className="qc-kappa__trow qc-kappa__trow--head" aria-hidden="true">
        <span>{t('kappa.table.trader')}</span>
        <span>{t('kappa.table.progress')}</span>
        <span className="qc-kappa__tnum">{t('kappa.table.done')}</span>
        {hasConds && <span className="qc-kappa__tcond">{t('kappa.table.need')}</span>}
      </div>
      {rows.map((r) => {
        const full = r.total > 0 && r.done === r.total
        return (
          <button
            key={r.traderId} type="button" aria-pressed={selected === r.traderId} onClick={() => onSelect(r.traderId)}
            className={cls('qc-kappa__trow', selected === r.traderId && 'is-on', full && 'is-full')}
          >
            <span className="qc-kappa__tname">{lookup.traderName(r.traderId)}</span>
            {r.total > 0
              ? <span className="qc-bar__track"><span className="qc-bar__fill" style={{ width: `${(r.done / r.total) * 100}%` }} /></span>
              : <span className="qc-muted">—</span>}
            <span className="qc-kappa__tnum">{r.total > 0 ? `${formatInt(r.done)} / ${formatInt(r.total)}` : '—'}</span>
            {hasConds && (
              <span className="qc-kappa__tcond">
                {r.conds.map((c) => (
                  <span key={c.kind} className={c.met ? 'qc-kappa__ok' : 'qc-warn'}>{condText(c, t)}</span>
                ))}
              </span>
            )}
          </button>
        )
      })}
    </section>
  )
}
