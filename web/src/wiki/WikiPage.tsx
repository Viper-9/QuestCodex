import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { Catalog } from '../api/catalog'
import { hashFor, replaceHash, type Route } from '../shell/router'
import { assignModColors, branchIndex, chainRank, countByTrader, DEFAULT_CHIPS, DEFAULT_SORT, filterQuests, inMods, listMods, makeLookup, orderTraders, sortQuests, toggleMember, type ChipKey, type Chips, type SortKey } from './derive'
import { TraderStrip } from './TraderStrip'
import { FilterBar } from './FilterBar'
import { ModStrip } from './ModStrip'
import { QuestDetail } from './QuestDetail'
import { QuestDescriptionDialog } from './QuestDescriptionDialog'
import { QuestPrepDialog } from './QuestPrepDialog'
import { QuestMapDialog } from './QuestMapDialog'
import { QuestList } from './QuestList'
import { rowId } from './QuestRow'
import { WikiSkeleton } from './WikiSkeleton'
import './wiki.css'

/** Mod 칩이 꺼졌을 때 모드 선택 대신 쓰는 빈 집합. 렌더마다 새로 만들면 useMemo 가 매번 다시 돈다 */
const EMPTY: ReadonlySet<string> = new Set()

interface WikiPageProps {
  catalog: Catalog | null
  route: Route
}

/** 필터·검색·펼침·팝업 상태의 소유자 (스펙 §2, §3.3). 아래 컴포넌트는 props 만 받는다. */
export function WikiPage({ catalog, route }: WikiPageProps) {
  const [traders, setTraders] = useState<ReadonlySet<string>>(() => new Set())
  const [chips, setChips] = useState<Chips>(DEFAULT_CHIPS)
  const [query, setQuery] = useState('')
  const [mods, setMods] = useState<ReadonlySet<string>>(() => new Set())
  const [sort, setSort] = useState<SortKey>(DEFAULT_SORT)
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  const [dialogId, setDialogId] = useState<string | null>(null)
  const [prepId, setPrepId] = useState<string | null>(null)
  const [mapId, setMapId] = useState<string | null>(null)
  /** 다음 커밋 후 scrollIntoView 할 행. 필터 리셋과 같은 렌더에 반영되므로 효과 시점엔 행이 DOM 에 있다. */
  const [scrollTarget, setScrollTarget] = useState<string | null>(null)
  const consumedDeepLink = useRef(false)

  // 파생값은 전부 useMemo (§3.2). 카탈로그가 바뀔 때(언어 전환)만 다시 계산된다.
  const quests = useMemo(() => (catalog ? Object.values(catalog.quests) : []), [catalog])
  const orderedTraders = useMemo(() => (catalog ? orderTraders(catalog.traders) : []), [catalog])
  const modList = useMemo(() => listMods(quests), [quests])
  /** 모드 칩 줄은 Mod 칩이 켜져 있을 때만 보이고, 그때만 모드 선택이 상인 개수·목록에 걸린다(filterQuests 와 같은 규칙) */
  const modsActive = chips.mod ? mods : EMPTY
  const traderBase = useMemo(() => inMods(quests, modsActive), [quests, modsActive])
  const counts = useMemo(() => countByTrader(traderBase), [traderBase])
  const lookup = useMemo(() => (catalog ? makeLookup(catalog) : null), [catalog])
  /** 필터가 아니라 `quests`(카탈로그 전체)로 계산 — 검색·필터에 따라 색이 바뀌면 안 된다. */
  const modColors = useMemo(() => assignModColors(quests), [quests])
  /** 연계순의 순위. `modColors` 와 같은 이유로 필터가 아니라 카탈로그 전체로 — 필터를 바꾸면 순서가 흔들린다. */
  const chainOrder = useMemo(() => chainRank(quests), [quests])
  /** 택일 분기. 역인덱스·연쇄 계산이라 카탈로그 전체로 한 번만 */
  const branches = useMemo(() => branchIndex(quests), [quests])
  const branchIds = useMemo(() => new Set(branches.keys()), [branches])
  const visible = useMemo(
    () => sortQuests(filterQuests(quests, { traders, chips, query, mods }), sort, chainOrder),
    [quests, traders, chips, query, mods, sort, chainOrder],
  )
  const visibleIds = useMemo(() => new Set(visible.map((q) => q.id)), [visible])

  /** 연계 링크·딥링크 공통 (§4.4): 안 보이면 필터 리셋 → 펼침 → 스크롤. URL 은 건드리지 않는다. */
  const jumpTo = useCallback((id: string) => {
    if (!visibleIds.has(id)) {
      setTraders(new Set())
      setChips(DEFAULT_CHIPS)
      setQuery('')
      setMods(new Set())
    }
    setExpanded((prev) => new Set(prev).add(id))
    setScrollTarget(id)
  }, [visibleIds])

  useEffect(() => {
    if (!scrollTarget) return
    document.getElementById(rowId(scrollTarget))?.scrollIntoView({ block: 'start', behavior: 'smooth' })
    setScrollTarget(null)
  }, [scrollTarget])

  /** 딥링크 #/wiki?quest=<id> (§4.5): 카탈로그 로딩 후 1회. 소비하면 URL 에서 quest 를 지운다. */
  useEffect(() => {
    if (!catalog || consumedDeepLink.current) return
    consumedDeepLink.current = true
    const id = route.query.get('quest')
    if (!id) return
    if (catalog.quests[id]) jumpTo(id)
    replaceHash(hashFor('wiki'))
  }, [catalog, route, jumpTo])

  if (!catalog || !lookup) return <WikiSkeleton />

  const toggleTrader = (id: string) => setTraders((prev) => toggleMember(prev, id))
  const toggleChip = (key: ChipKey) => setChips((prev) => ({ ...prev, [key]: !prev[key] }))
  const toggleMod = (key: string) => setMods((prev) => toggleMember(prev, key))
  const toggleExpanded = (id: string) => setExpanded((prev) => toggleMember(prev, id))

  return (
    <div className="qc-wiki">
      <TraderStrip
        traders={orderedTraders} counts={counts} total={traderBase.length}
        selected={traders} onToggle={toggleTrader} onClear={() => setTraders(new Set())}
      />
      <FilterBar
        query={query} onQueryChange={setQuery} chips={chips} onToggleChip={toggleChip}
        sort={sort} onSortChange={setSort}
      />
      {chips.mod && modList.length > 0 && (
        <ModStrip mods={modList} modColors={modColors} selected={mods} onToggle={toggleMod} onClear={() => setMods(new Set())} />
      )}
      <QuestList
        quests={visible}
        lookup={lookup}
        expanded={expanded}
        onToggle={toggleExpanded}
        modColors={modColors}
        branchIds={branchIds}
        renderDetail={(q) => (
          <QuestDetail quest={q} catalog={catalog} lookup={lookup} branch={branches.get(q.id)} onOpenDescription={setDialogId} onOpenPrep={setPrepId} onOpenMap={setMapId} onJump={jumpTo} />
        )}
      />
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
    </div>
  )
}
