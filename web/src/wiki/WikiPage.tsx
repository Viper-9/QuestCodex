import { useMemo, useState } from 'react'
import type { Catalog } from '../api/catalog'
import type { Route } from '../shell/router'
import { countByTrader, DEFAULT_CHIPS, filterQuests, makeLookup, orderTraders, sortQuests, toggleMember, type ChipKey, type Chips } from './derive'
import { TraderStrip } from './TraderStrip'
import { FilterBar } from './FilterBar'
import { WikiSkeleton } from './WikiSkeleton'
import './wiki.css'

interface WikiPageProps {
  catalog: Catalog | null
  route: Route
}

/** 필터·검색·펼침·팝업 상태의 소유자 (스펙 §2, §3.3). 아래 컴포넌트는 props 만 받는다. */
export function WikiPage({ catalog }: WikiPageProps) {
  const [traders, setTraders] = useState<ReadonlySet<string>>(() => new Set())
  const [chips, setChips] = useState<Chips>(DEFAULT_CHIPS)
  const [query, setQuery] = useState('')

  // 파생값은 전부 useMemo (§3.2). 카탈로그가 바뀔 때(언어 전환)만 다시 계산된다.
  const quests = useMemo(() => (catalog ? Object.values(catalog.quests) : []), [catalog])
  const orderedTraders = useMemo(() => (catalog ? orderTraders(catalog.traders) : []), [catalog])
  const counts = useMemo(() => countByTrader(quests), [quests])
  const lookup = useMemo(() => (catalog ? makeLookup(catalog) : null), [catalog])
  const visible = useMemo(
    () => sortQuests(filterQuests(quests, { traders, chips, query })),
    [quests, traders, chips, query],
  )

  if (!catalog || !lookup) return <WikiSkeleton />

  const toggleTrader = (id: string) => setTraders((prev) => toggleMember(prev, id))
  const toggleChip = (key: ChipKey) => setChips((prev) => ({ ...prev, [key]: !prev[key] }))

  return (
    <div className="qc-wiki">
      <TraderStrip
        traders={orderedTraders} counts={counts} total={quests.length}
        selected={traders} onToggle={toggleTrader} onClear={() => setTraders(new Set())}
      />
      <FilterBar query={query} onQueryChange={setQuery} chips={chips} onToggleChip={toggleChip} count={visible.length} />
      {/* Task 5 에서 QuestList 로 교체 */}
      <ul className="qc-placeholder">
        {visible.slice(0, 20).map((q) => <li key={q.id}>{q.name} · {lookup.traderName(q.traderId)} · {q.minLevel ?? '—'}</li>)}
      </ul>
    </div>
  )
}
