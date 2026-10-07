import type { ReactNode } from 'react'
import type { QuestStatus } from '../api/progress'
import { cls } from '../cls'
import type { T } from '../i18n/index'
import { useT } from '../i18n/I18nContext'
import type { NameLookup } from '../wiki/derive'
import { formatInt } from '../wiki/format'
import { itemDone, type KappaConds, type KappaItem, type TraderCond, type TraderRow } from './kappa'

// 카파 트래커 상단 — 왼쪽 종류별 막대(Collector 상태 한 줄), 오른쪽 상인 표. 합산 % 는 일부러 없다(스펙 §0: 가중치를 설명할 수 없어서).

interface SummaryProps {
  goalStatus: QuestStatus
  conds: KappaConds
  items: KappaItem[]
}

export function KappaSummary({ goalStatus, conds, items }: SummaryProps) {
  const t = useT()
  const itemsDone = items.filter(itemDone).length
  const tradersMet = conds.traders.filter((c) => c.met).length
  const allMet = (!conds.level || conds.level.met) && tradersMet === conds.traders.length
    && conds.quests.done >= conds.quests.total && itemsDone === items.length

  // 남은 수는 막대가 보여 주므로 따로 적지 않는다 — Collector 상태가 바뀌었을 때만 한 줄
  let head: string | null = null
  if (goalStatus === 'Success') head = t('kappa.goal.done')
  else if (goalStatus === 'Started' || goalStatus === 'AvailableForFinish') head = t('kappa.goal.started')
  else if (goalStatus === 'AvailableForStart' || allMet) head = t('kappa.goal.ready')

  return (
    <section className="qc-card qc-kappa__sum">
      {head && <p className="qc-kappa__head">{head}</p>}
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
    <li className={cls('qc-kappa__bar', full && 'is-full', done === 0 && 'is-zero')}>
      <span className="qc-kappa__barhead">
        <span>{label}</span>
        <span className="qc-kappa__barnum">
          <span className="qc-bar__done">{full ? '✓ ' : ''}{formatInt(done)}</span> <span className="qc-bar__total">/ {formatInt(total)}</span>
        </span>
      </span>
      <span className="qc-bar__track"><span className="qc-bar__fill" style={{ width: `${total > 0 ? (done / total) * 100 : 0}%` }} /></span>
    </li>
  )
}

/** 평판은 소수 둘째 자리까지, 끝의 0 은 뗀다 */
const num = (n: number) => String(Number(n.toFixed(2)))

/** 못 채운 조건은 "평판 2.34 / 3" — 현재 값 강조·"/ 필요"는 흐리게, 완료 칸 카운트(.qc-bar__num)와 같은 간격 */
function condText(c: TraderCond, t: T): ReactNode {
  if (c.met) return c.kind === 'loyalty' ? `${t('kappa.cond.ll', { need: c.need })} ✓` : `${t('kappa.cond.standing', { need: num(c.need) })} ✓`
  const current = c.current === null ? '?' : num(c.current)
  const need = c.kind === 'loyalty' ? String(c.need) : num(c.need)
  return <>
    <span className="qc-bar__done">{c.kind === 'loyalty' ? t('kappa.cond.llShort', { current }) : t('kappa.cond.standingShort', { current })}</span>
    {' '}<span className="qc-bar__total">/ {need}</span>
  </>
}

interface TradersProps {
  rows: TraderRow[]
  lookup: NameLookup
}

/** 상인 표 — 읽기 전용(거르기는 아래 상인 탭). 필요 등급 칸은 Collector 에 상인 조건이 있을 때만(sptQuestLive 형) */
export function KappaTraders({ rows, lookup }: TradersProps) {
  const t = useT()
  const hasConds = rows.some((r) => r.conds.length > 0)
  return (
    <section className={cls('qc-card qc-kappa__traders', hasConds && 'has-conds')}>
      <div className="qc-kappa__trow qc-kappa__trow--head" aria-hidden="true">
        <span>{t('kappa.table.trader')}</span>
        <span>{t('kappa.table.progress')}</span>
        <span className="qc-kappa__tnum"><span className="qc-bar__num"><span className="qc-kappa__tnumh">{t('kappa.table.done')}</span></span></span>
        {hasConds && <span className="qc-kappa__tcond">{t('kappa.table.need')}</span>}
      </div>
      {rows.map((r) => {
        const full = r.total > 0 && r.done === r.total
        const zero = r.total > 0 && r.done === 0
        return (
          <div key={r.traderId} className={cls('qc-kappa__trow', full && 'is-full', zero && 'is-zero')}>
            <span className="qc-kappa__tname">{lookup.traderName(r.traderId)}</span>
            {r.total > 0
              ? <span className="qc-bar__track"><span className="qc-bar__fill" style={{ width: `${(r.done / r.total) * 100}%` }} /></span>
              : <span className="qc-muted">—</span>}
            <span className="qc-kappa__tnum">
              {/* 카파 퀘스트가 없는 상인의 — 도 같은 칸 틀에 넣어 열 제목·숫자의 "/" 와 줄을 맞춘다 */}
              <span className="qc-bar__num">
                {r.total > 0
                  ? <><span className="qc-bar__done">{formatInt(r.done)}</span><span className="qc-bar__total">/ {formatInt(r.total)}</span></>
                  : <span className="qc-kappa__tnone">—</span>}
              </span>
            </span>
            {hasConds && (
              <span className="qc-kappa__tcond">
                {r.conds.map((c) => (
                  <span key={c.kind} className={c.met ? 'qc-kappa__ok' : 'qc-warn'}>{condText(c, t)}</span>
                ))}
              </span>
            )}
          </div>
        )
      })}
    </section>
  )
}

interface TraderTabsProps {
  rows: TraderRow[]
  lookup: NameLookup
  selected: string | null
  onSelect(traderId: string | null): void
  /** 탭 줄 오른쪽 끝에 붙일 것(목록 | 트리 세그먼트) */
  extra?: ReactNode
}

/**
 * 상인 탭 — 목록 바로 위에서 "상인별로 거를 수 있다"가 보이게 한다.
 * 모양은 레이드 준비 맵 탭(.qc-map)과 같고, 숫자는 남은 카파 퀘스트 수
 */
export function KappaTraderTabs({ rows, lookup, selected, onSelect, extra }: TraderTabsProps) {
  const t = useT()
  const left = (r: TraderRow) => r.total - r.done
  const tab = (id: string | null, name: string, n: number) => (
    <button
      key={id ?? ''} type="button" aria-pressed={selected === id} onClick={() => onSelect(id)}
      className={cls('qc-map', selected === id && 'is-on', n === 0 && 'is-empty')}
    >
      {name} <span className="qc-map__n">{formatInt(n)}</span>
    </button>
  )
  return (
    <div className="qc-maps" role="group" aria-label={t('kappa.table.trader')}>
      {tab(null, t('trader.all'), rows.reduce((sum, r) => sum + left(r), 0))}
      {rows.filter((r) => r.total > 0).map((r) => tab(r.traderId, lookup.traderName(r.traderId), left(r)))}
      {extra && <><span className="qc-maps__spacer" />{extra}</>}
    </div>
  )
}
