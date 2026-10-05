import { useMemo, useState } from 'react'
import type { Catalog } from '../api/catalog'
import type { Holding, ProfileProgress } from '../api/progress'
import { useT } from '../i18n/I18nContext'
import type { NameLookup } from '../wiki/derive'
import { chainStats, kappaConds, kappaDone, kappaGraph, kappaItems, kappaLists, traderRows } from './kappa'
import { KappaItems } from './KappaItems'
import { KappaQuests } from './KappaQuests'
import { KappaSummary, KappaTraders } from './KappaSummary'

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
  /** 상인 표에서 고른 상인 — 아래 퀘스트 목록만 거른다 */
  const [traderId, setTraderId] = useState<string | null>(null)

  const derived = useMemo(() => {
    if (!graph) return null
    const done = kappaDone(graph, progress)
    const conds = kappaConds(graph, progress, done)
    const stats = chainStats(graph, done)
    return { done, conds, stats, rows: traderRows(catalog, graph, done, conds.traders) }
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
        <KappaTraders rows={derived.rows} lookup={lookup} selected={traderId} onSelect={(id) => setTraderId(id === traderId ? null : id)} />
      </div>
      <KappaQuests
        catalog={catalog} graph={graph} progress={progress} lookup={lookup}
        lists={lists} done={derived.done} stats={derived.stats}
        filter={traderId ? { name: lookup.traderName(traderId), clear: () => setTraderId(null) } : null}
      />
      <KappaItems items={items} />
    </div>
  )
}
