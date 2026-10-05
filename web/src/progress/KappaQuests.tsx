import { useEffect, useMemo, useState, type ReactNode } from 'react'
import type { Catalog, CatalogQuest } from '../api/catalog'
import type { ProfileProgress } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { navigate } from '../shell/router'
import { branchIndex, toggleMember, type NameLookup } from '../wiki/derive'
import { formatInt } from '../wiki/format'
import { QuestDescriptionDialog } from '../wiki/QuestDescriptionDialog'
import { QuestDetail } from '../wiki/QuestDetail'
import { QuestMapDialog } from '../wiki/QuestMapDialog'
import { QuestPrepDialog } from '../wiki/QuestPrepDialog'
import { questProgress } from './derive'
import { requirementLines } from './format'
import type { ChainStat, KappaGraph, KappaLists } from './kappa'
import { KappaChainDialog } from './KappaChainDialog'
import { lineId, QuestLine } from './OverviewView'
import { objectiveLines } from './parts'

type Section = 'now' | 'locked' | 'done'

interface KappaQuestsProps {
  catalog: Catalog
  graph: KappaGraph
  progress: ProfileProgress
  lookup: NameLookup
  lists: KappaLists
  done: ReadonlySet<string>
  stats: ReadonlyMap<string, ChainStat>
  /** 상인 표에서 고른 상인. 칩으로 보여 주고 누르면 푼다 */
  filter: { name: string; clear(): void } | null
}

/** 카파 퀘스트 목록 — 지금 할 수 있는 것(펼침) / 잠김·완료(접힘). 줄과 펼친 상세는 현황 탭 것을 그대로 쓴다. */
export function KappaQuests({ catalog, graph, progress, lookup, lists, done, stats, filter }: KappaQuestsProps) {
  const t = useT()
  const [openSections, setOpenSections] = useState<ReadonlySet<Section>>(() => new Set(['now']))
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  const [dialogId, setDialogId] = useState<string | null>(null)
  const [prepId, setPrepId] = useState<string | null>(null)
  const [mapId, setMapId] = useState<string | null>(null)
  const [chainId, setChainId] = useState<string | null>(null)
  const [scrollTarget, setScrollTarget] = useState<string | null>(null)
  const branches = useMemo(() => branchIndex(Object.values(catalog.quests)), [catalog])

  /** 연계 링크: 카파 퀘스트면 그 구역을 열고 펼쳐 스크롤, 아니면(또는 상인 필터로 안 보이면) 위키로 */
  const jumpTo = (id: string) => {
    const section = (['now', 'locked', 'done'] as const).find((s) => lists[s].some((q) => q.id === id))
    if (!section) { navigate('wiki', null, { quest: id }); return }
    setOpenSections((prev) => new Set(prev).add(section))
    setExpanded((prev) => new Set(prev).add(id))
    setScrollTarget(id)
  }

  useEffect(() => {
    if (!scrollTarget) return
    document.getElementById(lineId(scrollTarget))?.scrollIntoView({ block: 'start', behavior: 'smooth' })
    setScrollTarget(null)
  }, [scrollTarget])

  const line = (q: CatalogQuest) => {
    const qp = questProgress(progress, q.id)
    const open = expanded.has(q.id)
    const s = stats.get(q.id)
    return (
      <QuestLine
        key={q.id} quest={q} qp={qp} lookup={lookup} open={open} flash={false}
        onToggle={() => setExpanded((prev) => toggleMember(prev, q.id))}
        tag={qp.status === 'FailRestartable' && <span className="qc-tag qc-warn">{t('kappa.failedRestart')}</span>}
        aside={
          <span className="qc-kappa__aside">
            {s && s.after > 0 && t('kappa.after', { n: formatInt(s.after) })}
            {s && s.chain > 0 && <> · {t('kappa.chain', { n: s.chain })}</>}
          </span>
        }
        detail={open && (
          <QuestDetail
            quest={q} catalog={catalog} lookup={lookup} branch={branches.get(q.id)}
            onOpenDescription={setDialogId} onOpenPrep={setPrepId} onOpenMap={setMapId} onJump={jumpTo}
            objectiveLines={objectiveLines(q, qp, t)}
            requirementLines={requirementLines(q, qp, lookup, t)}
            actions={<>
              <button type="button" className="qc-btn" onClick={() => setChainId(q.id)}>{t('kappa.openChain')}</button>
              <button type="button" className="qc-btn" onClick={() => navigate('wiki', null, { quest: q.id })}>{t('overview.openWiki')}</button>
            </>}
          />
        )}
      />
    )
  }

  const section = (key: Section, title: string, hint: string | null, quests: CatalogQuest[]): ReactNode => {
    const open = openSections.has(key)
    return (
      <div className={cls('qc-kappa__sec', open && 'is-open')}>
        <button type="button" className="qc-kappa__sechead" aria-expanded={open} onClick={() => setOpenSections((prev) => toggleMember(prev, key))}>
          <span className="qc-row__caret">{open ? '▾' : '▸'}</span>
          <span className="qc-kappa__sectitle">{title}</span>
          <span className="qc-group__n">{formatInt(quests.length)}</span>
          {hint && <span className="qc-kappa__sechint">· {hint}</span>}
        </button>
        {open && (quests.length === 0 ? <p className="qc-empty">{t('kappa.empty')}</p> : <ul>{quests.map(line)}</ul>)}
      </div>
    )
  }

  return (
    <section className="qc-card qc-card--flush qc-kappa__quests">
      {filter && (
        <div className="qc-kappa__filter">
          <button type="button" className="qc-chip is-on" onClick={filter.clear}>{filter.name} ✕</button>
          <span className="qc-muted">{t('kappa.filterHint')}</span>
        </div>
      )}
      {section('now', t('kappa.now'), t('kappa.nowHint'), lists.now)}
      {section('locked', t('kappa.locked'), t('kappa.lockedHint'), lists.locked)}
      {section('done', t('kappa.done'), null, lists.done)}
      <KappaChainDialog questId={chainId} catalog={catalog} graph={graph} progress={progress} done={done} lookup={lookup} onClose={() => setChainId(null)} />
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
