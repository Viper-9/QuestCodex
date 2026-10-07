import { useMemo, useState } from 'react'
import type { Catalog } from '../api/catalog'
import type { Holding, ProfileProgress } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import type { NameLookup } from '../wiki/derive'
import { usePersistedFlag } from '../wiki/useMapState'
import { chainStats, kappaConds, kappaDone, kappaGraph, kappaItems, kappaLists, kappaUnlocks, nothingOpen, traderRows } from './kappa'
import { KappaItems } from './KappaItems'
import { KappaQuests } from './KappaQuests'
import { KappaSummary, KappaTraderTabs, KappaTraders } from './KappaSummary'
import { KappaTree, KappaTreePick } from './KappaTree'

interface KappaViewProps {
  catalog: Catalog
  progress: ProfileProgress
  inventory: Record<string, Holding>
  lookup: NameLookup
}

/** 카파 트래커 — 위는 "얼마나 남았나"(남은 것·종류별 막대·상인 표), 아래는 "지금 뭘 하나"(카파 퀘스트 목록·제출 아이템). */
export function KappaView({ catalog, progress, inventory, lookup }: KappaViewProps) {
  const t = useT()
  const graph = useMemo(() => kappaGraph(catalog), [catalog])
  /** 상인 탭에서 고른 상인 — 아래 퀘스트 목록만 거른다 */
  const [traderId, setTraderId] = useState<string | null>(null)
  /** 목록 | 트리, 트리의 완료 접기 — 둘 다 다음에 열 때도 유지(상인 탭은 기억하지 않음) */
  const [tree, setTree] = usePersistedFlag('qc.kappa.tree', false)
  const [collapse, setCollapse] = usePersistedFlag('qc.kappa.collapse', false)

  const derived = useMemo(() => {
    if (!graph) return null
    const done = kappaDone(graph, progress)
    const conds = kappaConds(graph, progress, done)
    const stats = chainStats(graph, done)
    return { done, conds, stats, rows: traderRows(catalog, graph, done, conds.traders), unlocks: kappaUnlocks(catalog, graph, progress, done) }
  }, [catalog, graph, progress])
  const items = useMemo(() => (graph ? kappaItems(graph, progress, inventory) : []), [graph, progress, inventory])
  const lists = useMemo(
    () => (graph && derived ? kappaLists(catalog, graph, progress, derived.done, derived.stats, traderId) : null),
    [catalog, graph, progress, derived, traderId],
  )

  if (!graph || !derived || !lists) return <p className="qc-empty">{t('kappa.noCollector')}</p>

  return (
    <div className="qc-kappa">
      <div className="qc-kappa__top">
        <KappaSummary goalStatus={progress.quests[graph.goal.id]?.status ?? 'Locked'} conds={derived.conds} items={items} />
        <KappaTraders rows={derived.rows} lookup={lookup} />
      </div>
      <KappaTraderTabs rows={derived.rows} lookup={lookup} selected={traderId} onSelect={setTraderId} extra={(
        <span className="qc-ktree__seg" role="group" aria-label={t('kappa.view.label')}>
          <button type="button" aria-pressed={!tree} className={cls(!tree && 'is-on')} onClick={() => setTree(false)}>{t('kappa.view.list')}</button>
          <button type="button" aria-pressed={tree} className={cls(tree && 'is-on')} onClick={() => setTree(true)}>{t('kappa.view.tree')}</button>
        </span>
      )} />
      {!tree ? (
        <KappaQuests
          catalog={catalog} graph={graph} progress={progress} lookup={lookup}
          lists={lists} done={derived.done} stats={derived.stats} unlocks={derived.unlocks} fresh={nothingOpen(progress)}
        />
      ) : traderId ? (
        <KappaTree
          catalog={catalog} graph={graph} progress={progress} lookup={lookup} done={derived.done} stats={derived.stats}
          traderId={traderId} collapse={collapse} onCollapse={setCollapse}
        />
      ) : <KappaTreePick />}
      <KappaItems items={items} sources={catalog.lootSources} />
    </div>
  )
}
