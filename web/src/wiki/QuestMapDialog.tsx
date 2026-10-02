import { useEffect, useMemo, useRef, useState } from 'react'
import type { CatalogQuest, LockedDoor, MapExit } from '../api/catalog'
import { useT } from '../i18n/I18nContext'
import { formatObjective, lineText } from './format'
import { MapCanvas } from './MapCanvas'
import { mapName } from './mapNames'
import { MapToggle } from './MapToggle'
import { loadMapDef, loadMapIndex } from './mapAssets'
import { areaLevels, buildTabs, objectiveColor, doorsForTab, exitsForTab, firstLevel, fitView, layerFor, markerLevels, numberedObjectives, type MapDef, type MapIndex, type View } from './mapProjection'
import { useDialogFrame } from './useDialogFrame'
import { useLoaded, usePersistedFlag } from './useMapState'

interface QuestMapDialogProps {
  /** null 이면 닫힘. QuestPrepDialog 와 같은 showModal()/close() 토글. */
  quest: CatalogQuest | null
  traderName: string
  /** catalog.lockedDoors — 서버 맵 키 → 잠긴 문. 구버전 서버면 undefined. */
  lockedDoors: Record<string, LockedDoor[]> | undefined
  /** catalog.exits — 서버 맵 키 → 탈출구·환승. 구버전 서버면 undefined. */
  exits: Record<string, MapExit[]> | undefined
  /** catalog.mapVariants — 맵 교체 모드가 로드된 서버면 그 맵을 변형 지도로 그린다 */
  mapVariants: Record<string, string> | undefined
  onClose(): void
}

/** 위치정보 팝업. 맵 탭(+ 잠긴 문·탈출구 토글) → [지도(층 버튼·마커) | 목표 목록], 아래에 지도 출처. */
export function QuestMapDialog({ quest, traderName, lockedDoors, exits, mapVariants, onClose }: QuestMapDialogProps) {
  const t = useT()
  const ref = useRef<HTMLDialogElement>(null)
  const frame = useDialogFrame(ref, 'map', quest !== null, onClose)
  const index = useLoaded(quest ? () => loadMapIndex(mapVariants) : null, [quest !== null, mapVariants])

  useEffect(() => {
    const el = ref.current
    if (!el) return
    if (quest && !el.open) el.showModal()
    else if (!quest && el.open) el.close()
  }, [quest])

  return (
    <dialog ref={ref} className="qc-dialog qc-dialog--map" onClose={onClose} {...frame.dialogProps}>
      {frame.grips}
      {quest && (
        <header className="qc-dialog__head" {...frame.headProps}>
          <button type="button" className="qc-dialog__close" aria-label={t('dialog.close')} onClick={onClose}>✕</button>
          <h3 className="qc-dialog__title">{t('map.button')} · {quest.name}</h3>
          <p className="qc-dialog__meta">{traderName}</p>
        </header>
      )}
      {quest && (index.failed
        ? <p className="qc-map__msg qc-warn">{t('map.loadError')}</p>
        : index.data
          // key: 다른 퀘스트로 다시 열면 탭·층·확대 상태를 초기화한다(잠긴 문·탈출구 토글은 저장값이라 유지)
          ? <MapBody key={quest.id} quest={quest} index={index.data} lockedDoors={lockedDoors} exits={exits} />
          : <p className="qc-map__msg">{t('map.loading')}</p>)}
    </dialog>
  )
}

interface MapBodyProps {
  quest: CatalogQuest
  index: MapIndex
  lockedDoors: Record<string, LockedDoor[]> | undefined
  exits: Record<string, MapExit[]> | undefined
}

function MapBody({ quest, index, lockedDoors, exits }: MapBodyProps) {
  const t = useT()
  const tabs = useMemo(() => buildTabs(quest.objectives, index), [quest, index])
  const numbered = useMemo(() => numberedObjectives(quest.objectives), [quest])
  const [tabKey, setTabKey] = useState<string | null>(tabs[0]?.key ?? null)
  const [chosenLevel, setChosenLevel] = useState<number | null>(null)
  const [view, setView] = useState<View>(fitView)
  const [hot, setHot] = useState<number | null>(null)
  const [showDoors, setShowDoors] = usePersistedFlag('qc.map.showDoors', true)
  const [showExits, setShowExits] = usePersistedFlag('qc.map.showExits', true)
  const tab = tabs.find((x) => x.key === tabKey) ?? null
  const def = useLoaded(tabKey ? () => loadMapDef(tabKey) : null, [tabKey])
  const tabDoors = useMemo(() => (tabKey ? doorsForTab(lockedDoors, index, tabKey) : []), [lockedDoors, index, tabKey])
  const tabExits = useMemo(() => (tabKey ? exitsForTab(exits, index, tabKey) : []), [exits, index, tabKey])

  // 처음 열 때·탭을 바꿀 때는 첫 마커가 있는 층을 보여 준다
  const level = chosenLevel ?? (def.data && tab ? firstLevel(def.data, tab) : 0)
  const map = def.data
  // 영역이 있는 목표의 마커는 영역의 층을 따른다(08 스펙 §3.2)
  const markersHere = map && tab ? tab.markers.filter((m) => markerLevels(map, m, tab.areas).has(level)) : []
  const doorsHere = map && showDoors ? tabDoors.filter((d) => layerFor(map, d.position).level === level) : []
  // 탈출구는 층과 무관하게 다 보인다 — 다른 층의 것은 MapCanvas 가 흐리게 그린다
  const exitsShown = showExits ? tabExits : []
  // 영역은 높이 범위가 걸친 층들에서 보인다(구역 처치는 상자 높이 전체, 신호탄은 바닥 한 점)
  const areasHere = map && tab ? tab.areas.filter((a) => areaLevels(map, a.area).has(level)) : []

  function selectTab(key: string) {
    setTabKey(key)
    setChosenLevel(null)
    setView(fitView())
    setHot(null)
  }

  return (
    <>
      {tabs.length > 0 && (
        <div className="qc-map__tabs" role="tablist">
          {tabs.map((x) => (
            <button
              key={x.key} type="button" role="tab" aria-selected={x.key === tabKey}
              className={x.key === tabKey ? 'qc-map__tab is-on' : 'qc-map__tab'}
              onClick={() => selectTab(x.key)}
            >
              {mapName(x.key, t)}
            </button>
          ))}
          <MapToggle label={t('map.doors')} noneLabel={t('map.doorsNone')} on={showDoors} count={tabDoors.length} onToggle={setShowDoors} />
          <MapToggle label={t('map.exits')} noneLabel={t('map.exitsNone')} on={showExits} count={tabExits.length} onToggle={setShowExits} />
        </div>
      )}
      <div className="qc-map__body">
        <div className="qc-map__stage">
          {!tab && <p className="qc-map__msg">{t('map.noMapDef')}</p>}
          {tab && def.failed && <p className="qc-map__msg qc-warn">{t('map.loadError')}</p>}
          {tab && !def.failed && !def.data && <p className="qc-map__msg">{t('map.loading')}</p>}
          {tab && def.data && (
            <MapCanvas
              mapKey={tab.key} def={def.data} tab={tab} level={level} markers={markersHere} doors={doorsHere} exits={exitsShown} areas={areasHere}
              view={view} onView={setView} onLevel={(l) => { setChosenLevel(l); setHot(null) }}
              hot={hot} onHot={setHot}
            />
          )}
        </div>
        <aside className="qc-map__side">
          <h4 className="qc-detail__h">{t('map.objectives', { n: numbered.length })}</h4>
          <ol className="qc-map__list">
            {numbered.map(({ n, objective }) => {
              const here = markersHere.some((m) => m.n === n)
              return (
                <li
                  key={objective.conditionId}
                  className={[hot === n && 'is-hot', !here && 'is-off'].filter(Boolean).join(' ')}
                  onMouseEnter={() => setHot(n)}
                  onMouseLeave={() => setHot(null)}
                >
                  <span className="qc-map__num" style={{ ['--c' as string]: objectiveColor(n) }}>{n}</span>
                  <span>{lineText(formatObjective(objective, t))}</span>
                </li>
              )
            })}
          </ol>
          <p className="qc-map__hint">{t('map.hint')}</p>
        </aside>
      </div>
      {tab && def.data && <Credit def={def.data} />}
    </>
  )
}

/**
 * 지도 출처(05 스펙 §5): 원저작자, 수정자, 라이선스, 보정 데이터(map.json attribution.calibration, 없으면 DynamicMaps — 09 스펙의
 * 확장 인터체인지는 tarkov.dev). null 이면 보정 출처를 쓰지 않는다 — 수정자가 직접 맞춘 지도(11 스펙 아이스브레이커). 원본 라이선스 파일(.md)은 맵 폴더에
 * 동봉하지만 링크는 CC 원문으로 건다 — 정적 파일 서버가 모르는 확장자(.md)는 서빙하지 않을 수 있다.
 * 잠긴 문 아이콘(CC BY 3.0)의 출처는 여기 넣지 않는다 — THIRD_PARTY_NOTICES 와 maps/icons/marker_credits.txt(06 스펙 §5).
 */
const CC_BY_NC_SA = 'https://creativecommons.org/licenses/by-nc-sa/4.0/'

export function Credit({ def }: { def: MapDef }) {
  const t = useT()
  const a = def.attribution
  const author = a.modifiedBy ? `${a.author}, ${t('map.modifiedBy', { name: a.modifiedBy })}` : a.author
  return (
    <footer className="qc-map__credit">
      {a.calibration === null
        ? t('map.creditOwn', { author, license: a.license })
        : t('map.credit', { author, license: a.license, calibration: a.calibration ?? 'DynamicMaps (MIT)' })}
      {' · '}
      <a href={CC_BY_NC_SA} target="_blank" rel="noreferrer">{t('map.license')}</a>
    </footer>
  )
}
