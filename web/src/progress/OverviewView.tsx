import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type { Catalog, CatalogQuest } from '../api/catalog'
import type { Holding, ProfileProgress, QuestProgress } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { navigate } from '../shell/router'
import { assignModColors, branchIndex, listMods, orderTraders, toggleMember, type NameLookup } from '../wiki/derive'
import { ModStrip } from '../wiki/ModStrip'
import { QuestDescriptionDialog } from '../wiki/QuestDescriptionDialog'
import { QuestDetail } from '../wiki/QuestDetail'
import { QuestMapDialog } from '../wiki/QuestMapDialog'
import { QuestPrepDialog } from '../wiki/QuestPrepDialog'
import { TraderStrip } from '../wiki/TraderStrip'
import { formatObjective, lineText } from '../wiki/format'
import {
  countTabByTrader, countTabs, filterProgressQuests, firstOpenObjective, handoverReady, isUnreachable, QUEST_TABS, questProgress,
  questTab, tradersWithQuests, type QuestTab,
} from './derive'
import { lockReasonText, requirementLines } from './format'
import { Counter, objectiveLines, QuestLink } from './parts'

interface OverviewViewProps {
  catalog: Catalog
  progress: ProfileProgress
  inventory: Record<string, Holding>
  lookup: NameLookup
  highlight: ReadonlySet<string>
}

/** 현황 — "어디까지 왔나". 상인별 진행률(C4), 상인에게 갈 것(T1~T3), 상태별 퀘스트. */
export function OverviewView({ catalog, progress, inventory, lookup, highlight }: OverviewViewProps) {
  const t = useT()
  const quests = useMemo(() => Object.values(catalog.quests), [catalog])
  const traderIds = useMemo(
    () => tradersWithQuests(orderTraders(catalog.traders).map((x) => x.id), progress),
    [catalog, progress],
  )
  const turnIn = quests.filter((q) => questProgress(progress, q.id).status === 'AvailableForFinish')
  const accept = quests.filter((q) => questProgress(progress, q.id).status === 'AvailableForStart')
  const ready = useMemo(() => handoverReady(catalog, progress, inventory), [catalog, progress, inventory])

  return (
    <div className="qc-overview">
      <section className="qc-card">
        <h3 className="qc-card__h">{t('overview.traders')}</h3>
        <ul className="qc-bars">
          {traderIds.map((id) => {
            const s = progress.traderStats[id]
            const state = s.success === 0 ? ' is-zero' : s.success === s.total ? ' is-full' : ''
            return (
              <li key={id} className={`qc-bar${state}`}>
                <span className="qc-bar__name">{lookup.traderName(id)}</span>
                <span className="qc-bar__track"><span className="qc-bar__fill" style={{ width: `${(s.success / s.total) * 100}%` }} /></span>
                <span className="qc-bar__num">
                  <span className="qc-bar__done">{s.success}</span>
                  <span className="qc-bar__total">/ {s.total}</span>
                </span>
              </li>
            )
          })}
        </ul>
      </section>

      <section className="qc-card">
        <h3 className="qc-card__h">{t('overview.visit')}</h3>
        <QuestGroup title={t('overview.turnIn')} quests={turnIn} lookup={lookup} />
        <QuestGroup title={t('overview.accept')} quests={accept} lookup={lookup} />
        <div className="qc-group">
          <h4 className="qc-group__h">{t('overview.handover')} <span className="qc-group__n">{ready.length}</span></h4>
          <p className="qc-group__hint">{t('overview.handoverHint')}</p>
          {ready.length === 0 ? <p className="qc-muted">{t('overview.nothing')}</p> : (
            <ul className="qc-group__list">
              {ready.map((r) => (
                <li key={r.quest.id}>
                  <QuestLink quest={r.quest} />
                  <span className="qc-muted"> · {lookup.traderName(r.quest.traderId)} · {t('overview.handoverCount', { ready: r.ready, total: r.total })}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>

      <QuestTable catalog={catalog} progress={progress} lookup={lookup} traderOrder={traderIds} highlight={highlight} />
    </div>
  )
}

function QuestGroup({ title, quests, lookup }: { title: string; quests: CatalogQuest[]; lookup: NameLookup }) {
  const t = useT()
  return (
    <div className="qc-group">
      <h4 className="qc-group__h">{title} <span className="qc-group__n">{quests.length}</span></h4>
      {quests.length === 0 ? <p className="qc-muted">{t('overview.nothing')}</p> : (
        <ul className="qc-group__list">
          {quests.map((q) => (
            <li key={q.id}>
              <QuestLink quest={q} />
              <span className="qc-muted"> · {lookup.traderName(q.traderId)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

interface QuestTableProps {
  catalog: Catalog
  progress: ProfileProgress
  lookup: NameLookup
  traderOrder: string[]
  highlight: ReadonlySet<string>
}

/** 연계 점프로 펼친 줄을 강조해 두는 시간 */
const JUMP_FLASH_MS = 1500

export function lineId(questId: string): string {
  return `qc-pline-${questId}`
}

function QuestTable({ catalog, progress, lookup, traderOrder, highlight }: QuestTableProps) {
  const t = useT()
  const [tab, setTab] = useState<QuestTab>('active')
  const [traderIds, setTraderIds] = useState<ReadonlySet<string>>(() => new Set())
  const [query, setQuery] = useState('')
  const [mods, setMods] = useState<ReadonlySet<string>>(() => new Set())
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  const [dialogId, setDialogId] = useState<string | null>(null)
  const [prepId, setPrepId] = useState<string | null>(null)
  const [mapId, setMapId] = useState<string | null>(null)
  /** 다음 커밋 후 scrollIntoView 할 줄. 필터 리셋과 같은 렌더에 반영되므로 효과 시점엔 줄이 DOM 에 있다 (위키 jumpTo 와 같다). */
  const [scrollTarget, setScrollTarget] = useState<string | null>(null)
  const [jumped, setJumped] = useState<string | null>(null)
  const jumpTimer = useRef<number | undefined>(undefined)
  const counts = useMemo(() => countTabs(catalog, progress, mods), [catalog, progress, mods])
  // 위키와 같은 색이 나오도록 필터 결과가 아니라 카탈로그 전체로 정한다
  const modColors = useMemo(() => assignModColors(Object.values(catalog.quests)), [catalog])
  const modList = useMemo(() => listMods(Object.values(catalog.quests)), [catalog])
  /** 택일 분기. 위키와 같이 카탈로그 전체로 한 번만 */
  const branches = useMemo(() => branchIndex(Object.values(catalog.quests)), [catalog])
  const traderCounts = useMemo(() => countTabByTrader(catalog, progress, tab, mods), [catalog, progress, tab, mods])
  // 퀘스트가 하나도 없는 상인은 상인별 진행률과 같은 기준으로 뺀다
  const traders = useMemo(() => {
    const keep = new Set(traderOrder)
    return orderTraders(catalog.traders).filter((x) => keep.has(x.id))
  }, [catalog, traderOrder])
  const rows = useMemo(
    () => filterProgressQuests(catalog, progress, { tab, traderIds, query, mods }),
    [catalog, progress, tab, traderIds, query, mods],
  )
  const toggle = (id: string) => setExpanded((prev) => toggleMember(prev, id))

  /** 연계 링크: 지금 목록에 없으면 그 퀘스트의 탭으로 옮기고 상인·모드·검색 필터를 푼 뒤 펼침 → 스크롤 → 잠깐 강조 */
  const jumpTo = (id: string) => {
    if (!catalog.quests[id]) return
    if (!rows.some((q) => q.id === id)) {
      setTab(questTab(questProgress(progress, id).status))
      setTraderIds(new Set())
      setMods(new Set())
      setQuery('')
    }
    setExpanded((prev) => new Set(prev).add(id))
    setScrollTarget(id)
    setJumped(id)
    window.clearTimeout(jumpTimer.current)
    jumpTimer.current = window.setTimeout(() => setJumped(null), JUMP_FLASH_MS)
  }

  useEffect(() => {
    if (!scrollTarget) return
    document.getElementById(lineId(scrollTarget))?.scrollIntoView({ block: 'start', behavior: 'smooth' })
    setScrollTarget(null)
  }, [scrollTarget])

  useEffect(() => () => window.clearTimeout(jumpTimer.current), [])

  return (
    <section className="qc-card qc-card--flush">
      <TraderStrip
        traders={traders} counts={traderCounts} total={counts[tab]}
        selected={traderIds} onToggle={(id) => setTraderIds((prev) => toggleMember(prev, id))} onClear={() => setTraderIds(new Set())}
      />
      <div className="qc-card__bar">
        <h3 className="qc-card__h">{t('overview.quests')}</h3>
        <div className="qc-chips" role="group" aria-label={t('overview.quests')}>
          {QUEST_TABS.map((k) => (
            <button key={k} type="button" className={cls('qc-chip', tab === k && 'is-on')} aria-pressed={tab === k} onClick={() => setTab(k)}>
              {t(`qtab.${k}`)} {counts[k]}
            </button>
          ))}
        </div>
        <input
          className="qc-search"
          type="search"
          placeholder={t('overview.search')}
          aria-label={t('overview.search')}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </div>
      {modList.length > 0 && (
        <ModStrip
          mods={modList} modColors={modColors}
          selected={mods} onToggle={(key) => setMods((prev) => toggleMember(prev, key))} onClear={() => setMods(new Set())}
        />
      )}
      {rows.length === 0 ? <p className="qc-empty">{t('overview.empty')}</p> : (
        <ul>
          {rows.map((q) => {
            const qp = questProgress(progress, q.id)
            const open = expanded.has(q.id)
            return (
              <QuestLine
                key={q.id}
                quest={q}
                qp={qp}
                lookup={lookup}
                open={open}
                flash={highlight.has(q.id) || jumped === q.id}
                modColor={q.modName ? modColors[q.modName] : undefined}
                onToggle={() => toggle(q.id)}
                detail={open && (
                  <QuestDetail
                    quest={q} catalog={catalog} lookup={lookup} branch={branches.get(q.id)}
                    onOpenDescription={setDialogId} onOpenPrep={setPrepId} onOpenMap={setMapId} onJump={jumpTo}
                    objectiveLines={objectiveLines(q, qp, t)}
                    requirementLines={requirementLines(q, qp, lookup, t)}
                    actions={
                      <button type="button" className="qc-btn" onClick={() => navigate('wiki', null, { quest: q.id })}>{t('overview.openWiki')}</button>
                    }
                  />
                )}
              />
            )
          })}
        </ul>
      )}
      <QuestDescriptionDialog
        quest={dialogId ? catalog.quests[dialogId] ?? null : null}
        traderName={dialogId ? lookup.traderName(catalog.quests[dialogId]?.traderId ?? '') : ''}
        onClose={() => setDialogId(null)}
      />
      <QuestPrepDialog
        quest={prepId ? catalog.quests[prepId] ?? null : null}
        traderName={prepId ? lookup.traderName(catalog.quests[prepId]?.traderId ?? '') : ''}
        onClose={() => setPrepId(null)}
      />
      <QuestMapDialog
        quest={mapId ? catalog.quests[mapId] ?? null : null}
        traderName={mapId ? lookup.traderName(catalog.quests[mapId]?.traderId ?? '') : ''}
        lockedDoors={catalog.lockedDoors}
        exits={catalog.exits}
        mapVariants={catalog.mapVariants}
        onClose={() => setMapId(null)}
      />
    </section>
  )
}

interface QuestLineProps {
  quest: CatalogQuest
  qp: QuestProgress
  lookup: NameLookup
  open: boolean
  flash: boolean
  /** 모드 태그 색 번호 (위키와 같은 assignModColors) */
  modColor?: number
  onToggle(): void
  /** 펼쳤을 때 줄 아래에 붙는 위키와 같은 상세 */
  detail: ReactNode
  /** 이름 뒤 태그 자리에 덧붙일 것(카파 트래커의 "실패 — 다시 수락") */
  tag?: ReactNode
  /** 요약 뒤·카운터 앞에 덧붙일 것(카파 트래커의 "뒤로 N개 · 연쇄 N단계") */
  aside?: ReactNode
}

/** 한 줄 요약: 진행 중이면 첫 미완료 목표와 카운터, 잠김이면 첫 잠김 사유. 펼치면 위키와 같은 전체 상세. */
export function QuestLine({ quest, qp, lookup, open, flash, modColor, onToggle, detail, tag, aside }: QuestLineProps) {
  const t = useT()
  const locked = qp.status === 'Locked'
  const unreachable = locked && isUnreachable(qp)
  const first = firstOpenObjective(quest, qp)
  let summary: string | null = null
  if (locked) summary = qp.lockReasons[0] ? lockReasonText(qp.lockReasons[0], lookup, t) : null
  else if (qp.status === 'Started') summary = first ? lineText(formatObjective(first, t)) : t('overview.allDone')

  return (
    <li id={lineId(quest.id)} className={cls('qc-pline', open && 'is-open', flash && 'is-flash')}>
      <button type="button" className="qc-pline__row" aria-expanded={open} onClick={onToggle}>
        <span className="qc-pline__name">
          {quest.name}
          {!quest.isVanilla && (
            <span className="qc-tag qc-tag--mod" data-mod-color={modColor} title={quest.modName ?? undefined}>
              {quest.modName ?? t('tag.mod')}
            </span>
          )}
          {unreachable &&<span className="qc-tag qc-pline__tag" title={t('overview.unreachableHint')}>{t('overview.unreachable')}</span>}
          {tag}
        </span>
        <span className="qc-pline__trader">{lookup.traderName(quest.traderId)}</span>
        <span className="qc-pline__summary">{summary}</span>
        {aside}
        <span className="qc-pline__counter">{!locked && first && <Counter op={qp.objectives[first.conditionId]} />}</span>
      </button>
      {detail}
    </li>
  )
}
