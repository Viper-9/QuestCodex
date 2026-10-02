import { applyMapVariants, type MapDef, type MapIndex } from './mapProjection'

// public/maps/ 는 빌드 때 wwwroot 로 복사되어 /questcodex/maps/… 로 서빙된다(vite base = BASE_URL).
// 팝업에서 해당 맵을 열 때만 불러오고, 한 번 받은 것은 페이지 수명 동안 재사용한다. 실패는 캐시하지 않는다.

const BASE = `${import.meta.env.BASE_URL}maps`

export const mapAssetUrl = (key: string, file: string) => `${BASE}/${key}/${encodeURIComponent(file)}`

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url)
  if (!res.ok) throw new Error(`${url}: http${res.status}`)
  return (await res.json()) as T
}

let index: Promise<MapIndex> | null = null
const defs = new Map<string, Promise<MapDef>>()

/** mapVariants = catalog.mapVariants. 맵 교체 모드가 로드된 서버면 그 맵의 폴더 키가 변형 폴더로 바뀐다(09 스펙). */
export function loadMapIndex(mapVariants?: Record<string, string>): Promise<MapIndex> {
  index ??= fetchJson<MapIndex>(`${BASE}/index.json`).catch((e: unknown) => {
    index = null
    throw e
  })
  return index.then((i) => applyMapVariants(i, mapVariants))
}

export function loadMapDef(key: string): Promise<MapDef> {
  let def = defs.get(key)
  if (!def) {
    def = fetchJson<MapDef>(mapAssetUrl(key, 'map.json')).catch((e: unknown) => {
      defs.delete(key)
      throw e
    })
    defs.set(key, def)
  }
  return def
}
