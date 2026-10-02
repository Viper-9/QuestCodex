import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import type { LockedDoor } from '../api/catalog'
import { useT } from '../i18n/I18nContext'
import { MapCanvas } from '../wiki/MapCanvas'
import { loadMapDef, loadMapIndex } from '../wiki/mapAssets'
import { areaLevels, buildNumberedTabs, doorsForTab, firstLevel, fitView, layerFor, mapKeyFor, markerLevels, objectiveColor, type MapIndex, type MapTab, type View } from '../wiki/mapProjection'
import { Credit } from '../wiki/QuestMapDialog'
import { useDialogFrame } from '../wiki/useDialogFrame'
import { useLoaded, usePersistedFlag } from '../wiki/useMapState'
import type { RaidMapPlan } from './derive'

interface RaidMapProps {
  /** 고른 맵(서버 맵 키, 상단 맵 탭) */
  map: string
  mapLabel: string
  plan: RaidMapPlan
  /** catalog.lockedDoors — 구버전 서버면 undefined */
  lockedDoors: Record<string, LockedDoor[]> | undefined
  /** catalog.mapVariants — 맵 교체 모드가 로드된 서버면 그 맵을 변형 지도로 그린다 */
  mapVariants: Record<string, string> | undefined
  /** 강조할 퀘스트 번호 — 목록 줄과 지도 마커가 같은 값을 공유한다 */
  hot: number | null
  onHot(n: number | null): void
}

/**
 * 레이드 준비의 위치 지도. 위키 위치정보 팝업(QuestMapDialog)의 지도 부분과 같지만 맵 탭·목표 목록이 없다 —
 * 맵은 상단 맵 탭이 정하고, 목록은 "이 맵에서 진행되는 퀘스트" 카드가 대신한다(번호 = 퀘스트).
 * 크게 보기 버튼은 같은 지도를 옮기고 크기를 바꿀 수 있는 팝업으로 연다.
 */
export function RaidMap({ map, mapLabel, plan, lockedDoors, mapVariants, hot, onHot }: RaidMapProps) {
  const t = useT()
  const index = useLoaded(() => loadMapIndex(mapVariants), [mapVariants])
  const [showDoors, setShowDoors] = usePersistedFlag('qc.map.showDoors', true)
  const [expanded, setExpanded] = useState(false)
  const key = index.data ? mapKeyFor(index.data, map) : null
  const doors = useMemo(() => (index.data && key ? doorsForTab(lockedDoors, index.data, key) : []), [lockedDoors, index.data, key])
  const title = t('raid.map', { map: mapLabel })

  return (
    <section className="qc-card qc-raidmap">
      <div className="qc-raidmap__head">
        <h3 className="qc-card__h">{title}</h3>
        <button
          type="button"
          className={showDoors && doors.length > 0 ? 'qc-map__toggle is-on' : 'qc-map__toggle'}
          aria-pressed={showDoors}
          disabled={doors.length === 0}
          title={doors.length === 0 ? t('map.doorsNone') : undefined}
          onClick={() => setShowDoors(!showDoors)}
        >
          {t('map.doors')}
        </button>
        <button type="button" className="qc-map__toggle" disabled={key === null} onClick={() => setExpanded(true)}>
          {t('raid.mapExpand')}
        </button>
      </div>
      {index.failed && <p className="qc-map__msg qc-warn">{t('map.loadError')}</p>}
      {!index.failed && !index.data && <p className="qc-map__msg">{t('map.loading')}</p>}
      {index.data && key === null && <p className="qc-map__msg">{t('raid.mapNone')}</p>}
      {index.data && key !== null && (
        // key: 맵을 바꾸면 층·확대 상태를 초기화한다
        <MapView key={key} mapKey={key} index={index.data} plan={plan} doors={showDoors ? doors : []} hot={hot} onHot={onHot} />
      )}
      {index.data && key !== null && (
        <RaidMapDialog
          open={expanded} title={title} mapKey={key} index={index.data} plan={plan} doors={showDoors ? doors : []}
          onClose={() => setExpanded(false)}
        />
      )}
    </section>
  )
}

interface RaidMapDialogProps {
  open: boolean
  title: string
  mapKey: string
  index: MapIndex
  plan: RaidMapPlan
  doors: LockedDoor[]
  onClose(): void
}

/**
 * 크게 보기 팝업 — 위키 위치정보 팝업과 같은 틀(머리 드래그로 이동, 가장자리로 크기 조절, 틀 저장).
 * 모달이 아니다(show(), 백드롭 없음): 지도를 옆에 띄워 두고 페이지를 스크롤·클릭하며 쓰는 용도(사용자 요청).
 * 그래서 바깥 클릭으로 닫지 않고 ✕ 나 Esc 로 닫는다. 오른쪽에 번호 ↔ 퀘스트 범례를 둔다. 강조 상태는 팝업 안에서만 쓴다.
 * .qc-shell 로 포털한다 — 지도 카드(sticky)의 쌓임 맥락 밖에서 페이지 위에 뜨고, .qc-shell 의 색 토큰은 그대로 받는다.
 */
function RaidMapDialog({ open, title, mapKey, index, plan, doors, onClose }: RaidMapDialogProps) {
  const t = useT()
  const ref = useRef<HTMLDialogElement>(null)
  const frame = useDialogFrame(ref, 'raidmap', open, onClose)
  const [hot, setHot] = useState<number | null>(null)

  useEffect(() => {
    const el = ref.current
    if (!el) return
    if (open && !el.open) el.show()
    else if (!open && el.open) el.close()
  }, [open])

  // 모달이 아니면 브라우저가 Esc 로 닫아 주지 않는다
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  const legend = (
    <aside className="qc-map__side">
      <ol className="qc-map__list">
        {plan.quests.map(({ n, quest }) => (
          <li key={quest.id} className={hot === n ? 'is-hot' : undefined} onMouseEnter={() => setHot(n)} onMouseLeave={() => setHot(null)}>
            <span className="qc-map__num" style={{ ['--c' as string]: objectiveColor(n) }}>{n}</span>
            <span>{quest.name}</span>
          </li>
        ))}
      </ol>
    </aside>
  )

  return createPortal(
    <dialog ref={ref} className="qc-dialog qc-dialog--map qc-dialog--float" onClose={onClose}>
      {frame.grips}
      {open && (
        <>
          <header className="qc-dialog__head" {...frame.headProps}>
            <button type="button" className="qc-dialog__close" aria-label={t('dialog.close')} onClick={onClose}>✕</button>
            <h3 className="qc-dialog__title">{title}</h3>
          </header>
          {/* 열 때마다 새로 그려 전체 보기로 시작한다 */}
          <MapView mapKey={mapKey} index={index} plan={plan} doors={doors} hot={hot} onHot={setHot} side={legend} />
        </>
      )}
    </dialog>,
    document.querySelector('.qc-shell') ?? document.body,
  )
}

interface MapViewProps {
  mapKey: string
  index: MapIndex
  plan: RaidMapPlan
  doors: LockedDoor[]
  hot: number | null
  onHot(n: number | null): void
  /** 있으면 팝업 배치 — [지도 | side] + 출처. 없으면 카드 배치 — 지도 + 안내 + 출처. */
  side?: ReactNode
}

function MapView({ mapKey, index, plan, doors, hot, onHot, side }: MapViewProps) {
  const t = useT()
  const tab: MapTab = useMemo(
    () => buildNumberedTabs(plan.items, index).find((x) => x.key === mapKey) ?? { key: mapKey, markers: [], areas: [] },
    [plan, index, mapKey],
  )
  const [chosenLevel, setChosenLevel] = useState<number | null>(null)
  const [view, setView] = useState<View>(fitView)
  // 팝업 오른쪽 범례 접기 — 지도를 넓게 쓰려는 것이라 다음에 열 때도 유지
  const [sideOpen, setSideOpen] = usePersistedFlag('qc.raidmap.legend', true)
  const def = useLoaded(() => loadMapDef(mapKey), [mapKey])
  const map = def.data
  const level = chosenLevel ?? (map ? firstLevel(map, tab) : 0)
  // 층 거르기는 위키 팝업과 같은 규칙(영역이 있는 목표의 마커는 영역의 층, 08 스펙 §3.2)
  const markersHere = map ? tab.markers.filter((m) => markerLevels(map, m, tab.areas).has(level)) : []
  const doorsHere = map ? doors.filter((d) => layerFor(map, d.position).level === level) : []
  const areasHere = map ? tab.areas.filter((a) => areaLevels(map, a.area).has(level)) : []

  if (def.failed) return <p className="qc-map__msg qc-warn">{t('map.loadError')}</p>
  if (!map) return <p className="qc-map__msg">{t('map.loading')}</p>
  const canvas = (
    <MapCanvas
      mapKey={mapKey} def={map} tab={tab} level={level} markers={markersHere} doors={doorsHere} areas={areasHere}
      view={view} onView={setView} onLevel={(l) => { setChosenLevel(l); onHot(null) }}
      hot={hot} onHot={onHot}
    />
  )
  if (side) {
    return (
      <>
        <div className={sideOpen ? 'qc-map__body' : 'qc-map__body is-collapsed'}>
          <div className="qc-map__stage">
            {canvas}
            {/* 접혔을 때는 어두운 지도 위에서 눈에 띄게 강조색 — progress.css .qc-raidmap__side-btn */}
            <button
              type="button" className={sideOpen ? 'qc-raidmap__side-btn' : 'qc-raidmap__side-btn is-closed'}
              aria-expanded={sideOpen} onClick={() => setSideOpen(!sideOpen)}
            >
              {sideOpen ? t('raid.legendHide') : t('raid.legendShow', { n: plan.quests.length })}
            </button>
          </div>
          {sideOpen && side}
        </div>
        {/* 팝업 맨 아래 한 줄: 출처(왼쪽) · 조작 안내(오른쪽) */}
        <div className="qc-raidmap__foot">
          <Credit def={map} />
          <p className="qc-map__hint">{t('map.hint')}</p>
        </div>
      </>
    )
  }
  return (
    <>
      <div className="qc-raidmap__stage">{canvas}</div>
      <p className="qc-map__hint">{t('map.hint')}</p>
      <Credit def={map} />
    </>
  )
}
