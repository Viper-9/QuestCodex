import { useEffect, useMemo, useRef, type PointerEvent as ReactPointerEvent } from 'react'
import type { LockedDoor, MapExit, MapPoint } from '../api/catalog'
import { useT } from '../i18n/I18nContext'
import type { T } from '../i18n/index'
import { mapAssetUrl } from './mapAssets'
import { DoorMarker, ExitMarker } from './MapMarkers'
import {
  areaDrawOrder, areaPolygon, fitView, floorsWithOtherMarkers, layerFor, layerStyle, markerCountsByLevel, objectiveColor, project, zoomAt,
  type AreaMarker, type MapDef, type MapTab, type Marker, type View,
} from './mapProjection'

interface MapCanvasProps {
  mapKey: string
  def: MapDef
  tab: MapTab
  level: number
  /** 현재 층의 퀘스트 마커 */
  markers: Marker[]
  /** 현재 층의 잠긴 문. 토글이 꺼져 있으면 빈 배열. */
  doors: LockedDoor[]
  /** 탭의 모든 탈출구·환승(층 무관 — 다른 층의 것은 흐리게). 토글이 꺼져 있으면 빈 배열. */
  exits: MapExit[]
  /** 현재 층의 구역 영역(구역 처치·신호탄, 08 스펙) */
  areas: AreaMarker[]
  view: View
  onView(update: (v: View) => View): void
  onLevel(level: number): void
  hot: number | null
  onHot(n: number | null): void
}

/**
 * 층 SVG 를 겹친 캔버스를 뷰포트 안에 "contain" 으로 맞추고, CSS transform 으로 확대·이동한다.
 * 마커는 캔버스 안에 % 로 두고 1/배율로 되돌려 크기가 화면 기준으로 일정하다.
 * 쌓임 순서: 지도 < 구역 영역 < 잠긴 문 < 탈출구 < 퀘스트 마커 < 층 버튼.
 */
export function MapCanvas({ mapKey, def, tab, level, markers, doors, exits, areas, view, onView, onLevel, hot, onHot }: MapCanvasProps) {
  const t = useT()
  const viewportRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLDivElement>(null)
  const drag = useRef<{ id: number; x0: number; y0: number; v: View } | null>(null)
  const base = def.layers.find((l) => l.level === def.defaultLevel) ?? def.layers[0]
  const dotted = useMemo(() => floorsWithOtherMarkers(markerCountsByLevel(def, tab.markers, tab.areas), level), [def, tab, level])
  const floors = [...def.layers].sort((a, b) => b.level - a.level)
  const exitsByFloor = useMemo(
    () => exits.map((exit) => ({ exit, other: layerFor(def, exit.position).level !== level })).sort((a, b) => Number(b.other) - Number(a.other)),
    [exits, def, level],
  )

  // React 의 onWheel 은 passive 라 preventDefault 로 페이지 스크롤을 막을 수 없다 — 네이티브로 붙인다.
  useEffect(() => {
    const vp = viewportRef.current
    if (!vp) return
    const onWheel = (e: WheelEvent) => {
      const canvas = canvasRef.current
      if (!canvas) return
      e.preventDefault()
      const r = vp.getBoundingClientRect()
      // 캔버스의 변환 전 원점(offsetLeft/Top) 기준 좌표. transform 은 offset 에 영향을 주지 않는다.
      const cx = e.clientX - r.left - canvas.offsetLeft
      const cy = e.clientY - r.top - canvas.offsetTop
      onView((v) => zoomAt(v, e.deltaY < 0 ? 1.25 : 0.8, cx, cy))
    }
    vp.addEventListener('wheel', onWheel, { passive: false })
    return () => vp.removeEventListener('wheel', onWheel)
  }, [onView])

  function onPointerDown(e: ReactPointerEvent<HTMLDivElement>) {
    if (e.button !== 0 || (e.target as HTMLElement).closest('button')) return
    e.currentTarget.setPointerCapture(e.pointerId)
    drag.current = { id: e.pointerId, x0: e.clientX, y0: e.clientY, v: view }
  }
  function onPointerMove(e: ReactPointerEvent<HTMLDivElement>) {
    const d = drag.current
    if (!d || d.id !== e.pointerId) return
    onView(() => ({ ...d.v, x: d.v.x + e.clientX - d.x0, y: d.v.y + e.clientY - d.y0 }))
  }
  const endDrag = () => { drag.current = null }

  /** 마커 공통 배치: 점이 속한 층 SVG 기준 % 위치 + 배율 되돌리기 */
  const place = (point: MapPoint) => {
    const layer = layerFor(def, point)
    const p = project(def, layer, point)
    return {
      left: `${(p.x / layer.viewBox.width) * 100}%`,
      top: `${(p.y / layer.viewBox.height) * 100}%`,
      transform: `translate(-50%, -50%) scale(${1 / view.scale})`,
    }
  }

  return (
    <div
      ref={viewportRef}
      className="qc-map__viewport"
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onDoubleClick={(e) => { if (!(e.target as HTMLElement).closest('button')) onView(fitView) }}
    >
      <div
        ref={canvasRef}
        className="qc-map__canvas"
        style={{
          ['--ar' as string]: base.viewBox.width / base.viewBox.height,
          transform: `translate(${view.x}px, ${view.y}px) scale(${view.scale})`,
        }}
      >
        {def.layers.map((layer) => {
          const s = layerStyle(layer.level, level, def.defaultLevel)
          if (!s.visible) return null
          return (
            <img
              key={layer.svg} className="qc-map__layer" src={mapAssetUrl(mapKey, layer.svg)} alt="" draggable={false}
              style={{ filter: s.brightness < 1 ? `brightness(${s.brightness})` : undefined, opacity: s.opacity }}
            />
          )
        })}
        {areas.length > 0 && (
          // 영역은 기본 층 SVG 와 같은 viewBox 의 SVG 한 장에 그린다. 층마다 imageBounds 가 같아서 한 좌표계로 충분하다.
          <svg className="qc-map__areas" viewBox={`0 0 ${base.viewBox.width} ${base.viewBox.height}`} preserveAspectRatio="none" aria-hidden>
            {/* 넓은 영역부터 — 작은 영역이 위에 온다. 색은 목표별(같은 목표의 영역은 같은 색) */}
            {areaDrawOrder(areas).map((a, i) => (
              <polygon
                key={i}
                className={`qc-map__area${emphasis(hot, a.n)}`}
                style={{ ['--c' as string]: objectiveColor(a.n) }}
                points={areaPolygon(def, base, a.area).map((p) => `${p.x},${p.y}`).join(' ')}
              />
            ))}
          </svg>
        )}
        {doors.map((d, i) => <DoorMarker key={`d${i}`} door={d} style={place(d.position)} />)}
        {/* 다른 층의 탈출구를 먼저 — 현재 층의 것이 위에 온다 */}
        {exitsByFloor.map(({ exit, other }, i) => <ExitMarker key={`e${i}`} exit={exit} other={other} style={place(exit.position)} />)}
        {markers.map((m, i) => (
          <span
            key={i}
            className={`qc-map__marker${emphasis(hot, m.n)}`}
            style={{ ...place(m.point), ['--c' as string]: objectiveColor(m.n) }}
            onMouseEnter={() => onHot(m.n)}
            onMouseLeave={() => onHot(null)}
          >
            {m.n}
          </span>
        ))}
      </div>
      {floors.length > 1 && (
        <div className="qc-map__floors" role="group" aria-label={t('map.floors')}>
          {floors.map((f) => {
            const dot = dotted.has(f.level)
            const name = floorName(f.level, t)
            return (
              <button
                key={f.level} type="button"
                className={f.level === level ? 'qc-map__floor is-on' : 'qc-map__floor'}
                aria-pressed={f.level === level}
                aria-label={dot ? `${name} · ${t('map.floorHasMarkers')}` : name}
                onClick={() => onLevel(f.level)}
              >
                {name}
                {dot && <span className="qc-map__dot" aria-hidden />}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}

/** 강조 클래스: 마우스를 올린 목표는 is-hot, 그동안 다른 목표는 is-dim(흐리게), 아무것도 안 올렸으면 없음. */
function emphasis(hot: number | null, n: number): string {
  if (hot === null) return ''
  return hot === n ? ' is-hot' : ' is-dim'
}

function floorName(level: number, t: T): string {
  if (level < 0) return t('map.floor.underground')
  if (level === 0) return t('map.floor.ground')
  return t('map.floor.upper', { n: level + 1 })
}
