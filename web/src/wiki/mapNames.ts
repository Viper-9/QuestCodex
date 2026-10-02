import type { T, UiKey } from '../i18n/index'

const MAP_NAMES = new Set([
  'bigmap', 'factory4_day', 'sandbox', 'interchange', 'interchange-manimal', 'laboratory', 'labyrinth',
  'lighthouse', 'rezervbase', 'shoreline', 'tarkovstreets', 'woods',
])

/** 탭 이름. 번역이 없는 새 맵 폴더면 폴더 키 그대로. */
export function mapName(key: string, t: T): string {
  return MAP_NAMES.has(key) ? t(`map.name.${key}` as UiKey) : key
}

/** 서버 맵 키(환승 목적지)의 이름. 짝 맵(공장 야간, 그라운드 제로 고레벨)은 같은 이름을 쓴다. */
export function serverMapName(key: string, t: T): string {
  return mapName(key === 'factory4_night' ? 'factory4_day' : key.replace(/_high$/, ''), t)
}
