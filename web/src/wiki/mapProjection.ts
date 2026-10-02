import type { LockedDoor, MapArea, MapPoint, Objective } from '../api/catalog'

// 위치정보 팝업의 계산부. React 없이 테스트된다(mapProjection.test.ts).
// 맵 정의(public/maps/<key>/map.json)는 tools/maps/build-maps.js 가 DynamicMaps 의 jsonc 에서 만든 것이고,
// 축 이름은 이미 Unity 기준(y = 높이)으로 바뀌어 있다. 계산은 DynamicMaps MapView/MapLayer 와 같다.

export interface FlatPoint { x: number; z: number }
export interface Bounds3 { min: MapPoint; max: MapPoint }

export interface MapLayerDef {
  name: string
  level: number
  svg: string
  viewBox: { width: number; height: number }
  imageBounds: { min: FlatPoint; max: FlatPoint }
  gameBounds: Bounds3[]
}

export interface MapAttribution {
  author: string
  authorLink: string
  modifiedBy: string | null
  license: string
  licenseFile: string
  /** 좌표 보정 데이터 출처(크레딧 표기). 없으면 DynamicMaps — build-maps.js 산출물은 이 필드를 쓰지 않는다 */
  calibration?: string
}

export interface MapDef {
  displayName: string
  internalNames: string[]
  coordinateRotation: number
  defaultLevel: number
  layers: MapLayerDef[]
  attribution: MapAttribution
  source: string
}

export interface MapIndex {
  /** variants: 변형 ID → 그 변형의 맵 폴더 키(09 스펙, 예: manimal → interchange-manimal) */
  maps: { key: string; internalNames: string[]; variants?: Record<string, string> }[]
}

/**
 * 서버가 알려 준 활성 변형(catalog.mapVariants: 서버 맵 키 → 변형 ID)을 index 에 적용한다. 변형 폴더가 있는 항목은
 * key 만 그 폴더로 바꾸므로 mapKeyFor·탭·잠긴 문이 그대로 변형 지도를 쓴다. 변형이 없거나 모르는 ID 면 그대로.
 */
export function applyMapVariants(index: MapIndex, active: Record<string, string> | undefined): MapIndex {
  if (!active || Object.keys(active).length === 0) return index
  return {
    ...index,
    maps: index.maps.map((d) => {
      const variant = d.internalNames.map((n) => active[n.toLowerCase()]).find((v) => v !== undefined)
      const folder = variant === undefined ? undefined : d.variants?.[variant]
      return folder ? { ...d, key: folder } : d
    }),
  }
}

/** 마커 하나. n = 위치가 있는 목표 중 몇 번째인가(1부터). 한 목표에 점이 여럿이면 같은 n 이 여럿. */
export interface Marker {
  n: number
  conditionId: string
  point: MapPoint
}

/** 구역 영역 하나(08 스펙). n 은 같은 목표의 번호 마커와 같다. */
export interface AreaMarker {
  n: number
  conditionId: string
  area: MapArea
}

export interface MapTab {
  /** 맵 폴더 키(public/maps/<key>) */
  key: string
  markers: Marker[]
  areas: AreaMarker[]
}

/** Unity (x, z) 를 반시계로 deg 만큼 돌린다. */
function rotate(x: number, z: number, deg: number): [number, number] {
  const r = (deg * Math.PI) / 180
  const cos = Math.cos(r)
  const sin = Math.sin(r)
  return [x * cos - z * sin, x * sin + z * cos]
}

/**
 * Unity 좌표 → 층 SVG 의 viewBox 좌표. 회전한 imageBounds 네 꼭짓점의 범위를 SVG 전체에 비례 대응시킨다.
 * SVG 는 y 가 아래로 커지므로 세로는 뒤집는다.
 */
export function project(def: MapDef, layer: MapLayerDef, p: MapPoint): { x: number; y: number } {
  const { min, max } = layer.imageBounds
  const corners = [[min.x, min.z], [min.x, max.z], [max.x, min.z], [max.x, max.z]]
    .map(([x, z]) => rotate(x, z, def.coordinateRotation))
  const xs = corners.map((c) => c[0])
  const ys = corners.map((c) => c[1])
  const [minX, maxX, minY, maxY] = [Math.min(...xs), Math.max(...xs), Math.min(...ys), Math.max(...ys)]
  const [rx, ry] = rotate(p.x, p.z, def.coordinateRotation)
  return {
    x: ((rx - minX) / (maxX - minX)) * layer.viewBox.width,
    y: ((maxY - ry) / (maxY - minY)) * layer.viewBox.height,
  }
}

const contains = (b: Bounds3, p: MapPoint) =>
  p.x >= b.min.x && p.x <= b.max.x && p.y >= b.min.y && p.y <= b.max.y && p.z >= b.min.z && p.z <= b.max.z

const volume = (b: Bounds3) => (b.max.x - b.min.x) * (b.max.y - b.min.y) * (b.max.z - b.min.z)

/** 점이 속한 층: 점을 포함하는 gameBounds 중 부피가 가장 작은 것의 층. 어디에도 없으면 기본 층. */
export function layerFor(def: MapDef, p: MapPoint): MapLayerDef {
  let best: MapLayerDef | null = null
  let bestVolume = Infinity
  for (const layer of def.layers) {
    for (const b of layer.gameBounds) {
      if (contains(b, p) && volume(b) < bestVolume) {
        best = layer
        bestVolume = volume(b)
      }
    }
  }
  return best ?? defaultLayer(def)
}

export function defaultLayer(def: MapDef): MapLayerDef {
  return def.layers.find((l) => l.level === def.defaultLevel) ?? def.layers[0]
}

/**
 * 층 SVG 를 겹쳐 그리는 방식(DynamicMaps MapLayer.OnTopLevelSelected). 층 SVG 에는 그 층의 도형만 있어서
 * 2층만 그리면 건물 조각만 떠 보인다 — 아래층을 어둡게 깔아 준다.
 */
export function layerStyle(level: number, selected: number, defaultLevel: number) {
  const brightness = Math.min(1, 0.5 ** (selected - level))
  if (level <= selected) return { visible: true, brightness, opacity: 1 }
  if (level === defaultLevel) return { visible: true, brightness, opacity: 0.1 }
  return { visible: false, brightness, opacity: 1 }
}

/** 서버 맵 키(bigmap, sandbox_high …) → 맵 폴더 키. internalNames 와 대소문자를 무시하고 대조한다. */
export function mapKeyFor(index: MapIndex, map: string): string | null {
  const m = map.toLowerCase()
  return index.maps.find((d) => d.internalNames.some((n) => n.toLowerCase() === m))?.key ?? null
}

/**
 * 목표들의 위치를 맵 정의 단위 탭으로 묶는다. 탭 순서 = 목표 순서에서 처음 나온 순. 정의가 없는 맵은 버린다.
 * 짝 맵(factory4_day·night, sandbox·sandbox_high)은 한 탭이 되는데, 같은 존이 양쪽에 같은 좌표로 있는 경우가
 * 대부분이라 같은 목표의 같은 점은 한 번만 넣는다 — 안 그러면 마커가 겹쳐 찍히고 층 버튼 개수가 두 배가 된다.
 */
export function buildTabs(objectives: Objective[], index: MapIndex): MapTab[] {
  return buildNumberedTabs(numberedObjectives(objectives), index)
}

/**
 * 번호를 미리 정한 목표들로 탭을 만든다. 여러 목표가 같은 번호를 가질 수 있다 — 진행현황 레이드 지도는 퀘스트마다
 * 번호 하나라 그 퀘스트의 목표들이 같은 번호·색으로 찍힌다. 위치가 없는 목표는 건너뛴다.
 */
export function buildNumberedTabs(items: { n: number; objective: Objective }[], index: MapIndex): MapTab[] {
  const tabs = new Map<string, MapTab>()
  for (const { n, objective: o } of items) {
    const locations = o.locations ?? []
    for (const loc of locations) {
      const key = mapKeyFor(index, loc.map)
      if (key === null) continue
      if (!tabs.has(key)) tabs.set(key, { key, markers: [], areas: [] })
      const tab = tabs.get(key)!
      for (const point of loc.points) {
        const dup = tab.markers.some((m) => m.n === n && samePoint(m.point, point))
        if (!dup) tab.markers.push({ n, conditionId: o.conditionId, point })
      }
      // 영역도 짝 맵에서 같은 것이 두 번 오므로 한 번만(05 §4.3 과 같은 이유)
      for (const area of loc.areas ?? []) {
        const dup = tab.areas.some((a) => a.n === n && sameArea(a.area, area))
        if (!dup) tab.areas.push({ n, conditionId: o.conditionId, area })
      }
    }
  }
  return [...tabs.values()]
}

const samePoint = (a: MapPoint, b: MapPoint) => a.x === b.x && a.y === b.y && a.z === b.z
const sameArea = (a: MapArea, b: MapArea) =>
  samePoint(a.center, b.center) && a.sizeX === b.sizeX && a.sizeZ === b.sizeZ && a.yaw === b.yaw

/**
 * 영역 사각형의 네 꼭짓점(Unity x·z). 로컬 (±폭/2) 를 yaw 만큼 돌린다 — Unity 규약(왼손 좌표계, y 위)이라 위에서 볼 때
 * 양수 yaw 가 시계 방향: 로컬 (lx, lz) → 월드 (lx·cosθ + lz·sinθ, −lx·sinθ + lz·cosθ). 순서는 사각형 둘레 순.
 */
export function areaCorners(area: MapArea): FlatPoint[] {
  const r = (area.yaw * Math.PI) / 180
  const cos = Math.cos(r)
  const sin = Math.sin(r)
  const hx = area.sizeX / 2
  const hz = area.sizeZ / 2
  return [[-hx, -hz], [hx, -hz], [hx, hz], [-hx, hz]].map(([lx, lz]) => ({
    x: area.center.x + lx * cos + lz * sin,
    z: area.center.z - lx * sin + lz * cos,
  }))
}

/** 영역 네 꼭짓점을 층 SVG 의 viewBox 좌표로. 맵 회전은 project 가 처리한다. */
export function areaPolygon(def: MapDef, layer: MapLayerDef, area: MapArea): { x: number; y: number }[] {
  return areaCorners(area).map((c) => project(def, layer, { x: c.x, y: area.center.y, z: c.z }))
}

/** 위치가 있는 목표만, 마커 번호와 함께 */
export function numberedObjectives(objectives: Objective[]): { n: number; objective: Objective }[] {
  return objectives.filter((o) => (o.locations ?? []).length > 0).map((objective, i) => ({ n: i + 1, objective }))
}

/**
 * 탭(맵 폴더 키)에 그릴 잠긴 문. 그 맵 정의의 internalNames 에 해당하는 서버 맵 키의 문을 모두 모은다.
 * 짝 맵(공장 주간·야간 등)은 같은 문이 같은 좌표로 두 번 오므로 열쇠 + 좌표가 같으면 한 번만.
 */
export function doorsForTab(doors: Record<string, LockedDoor[]> | undefined, index: MapIndex, key: string): LockedDoor[] {
  const out: LockedDoor[] = []
  for (const [map, list] of Object.entries(doors ?? {})) {
    if (mapKeyFor(index, map) !== key) continue
    for (const d of list) {
      const p = d.position
      const dup = out.some((o) => o.keyTpl === d.keyTpl && o.position.x === p.x && o.position.y === p.y && o.position.z === p.z)
      if (!dup) out.push(d)
    }
  }
  return out
}

/** 층 버튼의 알림 점: 현재 층이 아니면서 퀘스트 마커가 있는 층 */
export function floorsWithOtherMarkers(counts: Map<number, number>, level: number): Set<number> {
  return new Set([...counts].filter(([l, n]) => l !== level && n > 0).map(([l]) => l))
}

/** 층 버튼의 알림 점 판정용: level → 그 층에 찍힐 퀘스트 마커 수 */
export function markerCountsByLevel(def: MapDef, markers: Marker[], areas: AreaMarker[] = []): Map<number, number> {
  const counts = new Map<number, number>()
  for (const m of markers) {
    for (const level of markerLevels(def, m, areas)) counts.set(level, (counts.get(level) ?? 0) + 1)
  }
  return counts
}

/**
 * 영역이 걸친 층들(08 스펙 §3.2). 영역 중심 x·z 기둥에서 minY~maxY 를 1m 간격으로 짚어 layerFor 의 층을 모은다.
 * 구역 처치는 상자 높이 전체, 신호탄은 서버가 바닥 한 점으로 줄여 보낸다. 범위가 없으면(구버전 서버) center.y 한 점.
 */
export function areaLevels(def: MapDef, area: MapArea): Set<number> {
  const lo = area.minY ?? area.center.y
  const hi = area.maxY ?? area.center.y
  const levels = new Set<number>()
  const at = (y: number) => levels.add(layerFor(def, { x: area.center.x, y, z: area.center.z }).level)
  for (let y = lo; y < hi; y += 1) at(y)
  at(hi)
  return levels
}

/** 목표 색 팔레트 크기 — styles.css 의 --obj-1 … --obj-10 과 반드시 일치. */
export const OBJECTIVE_COLOR_COUNT = 10

/**
 * 목표 번호 → 색(CSS 변수). 한 목표의 영역·마커·목록 번호는 같은 색, 다른 목표는 다른 색이라 겹친 영역을 구분한다.
 * 10개를 넘으면 순환한다.
 */
export function objectiveColor(n: number): string {
  return `var(--obj-${((n - 1) % OBJECTIVE_COLOR_COUNT) + 1})`
}

/** 그리는 순서: 넓은 영역부터 — 나중에 그린 작은 영역(호텔 킬존 안의 안뜰 신호탄 등)이 위에 보인다. */
export function areaDrawOrder(areas: AreaMarker[]): AreaMarker[] {
  return [...areas].sort((a, b) => b.area.sizeX * b.area.sizeZ - a.area.sizeX * a.area.sizeZ)
}

/** 팝업을 열거나 탭을 바꿨을 때 보여 줄 층: 첫 마커가 보이는 층 중 기본 층(지상)이 있으면 그것, 없으면 가장 낮은 층. */
export function firstLevel(def: MapDef, tab: MapTab): number {
  if (tab.markers.length === 0) return def.defaultLevel
  const levels = markerLevels(def, tab.markers[0], tab.areas)
  return levels.has(def.defaultLevel) ? def.defaultLevel : Math.min(...levels)
}

/** 마커가 보이는 층: 같은 목표(n)에 영역이 있으면 그 영역들의 층 합집합(점 높이는 무시), 없으면 점의 층 하나. */
export function markerLevels(def: MapDef, marker: Marker, areas: AreaMarker[]): Set<number> {
  const own = areas.filter((a) => a.n === marker.n)
  if (own.length === 0) return new Set([layerFor(def, marker.point).level])
  const levels = new Set<number>()
  for (const a of own) for (const l of areaLevels(def, a.area)) levels.add(l)
  return levels
}

/** 확대·이동 상태. 내용(지도) 좌표 c 는 화면에서 c * scale + (x, y) 에 그려진다. */
export interface View { scale: number; x: number; y: number }

const MIN_SCALE = 1
const MAX_SCALE = 12

export function fitView(): View {
  return { scale: 1, x: 0, y: 0 }
}

/** 화면 좌표 (cx, cy) 아래의 지점을 고정한 채 factor 배 확대·축소한다. 1배로 돌아오면 이동도 초기화한다. */
export function zoomAt(v: View, factor: number, cx: number, cy: number): View {
  const scale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, v.scale * factor))
  if (scale === MIN_SCALE) return fitView()
  const k = scale / v.scale
  return { scale, x: cx - (cx - v.x) * k, y: cy - (cy - v.y) * k }
}
