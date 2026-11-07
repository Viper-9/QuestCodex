import type { CSSProperties } from 'react'
import type { LockedDoor, MapExit } from '../api/catalog'
import { useT } from '../i18n/I18nContext'
import type { T, UiKey } from '../i18n/index'
import { doorLabel } from './format'
import { mapAssetUrl } from './mapAssets'
import { serverMapName } from './mapNames'
import type { TabBossEntry, TabBossZone, TabBosses } from './mapProjection'

interface DoorMarkerProps {
  door: LockedDoor
  style: CSSProperties
  /**
   * 보유 열쇠 표시 자리(06 스펙 §4.3). 위키는 프로필과 무관해서 지금은 넘기는 곳이 없다 — 진행현황 페이지에서
   * 프로필 인벤토리와 door.keyTpl 을 대조해 넘기면 is-owned / is-missing 클래스가 붙는다.
   */
  owned?: boolean
}

/**
 * 잠긴 문: DynamicMaps 아이콘(흰 도형 + 검은 외곽선)에 DynamicMaps 처럼 색을 곱한다 — 흰 부분만 물들고 외곽선은
 * 검은 채로 남는다(LockedDoorMarkerMutator: 열쇠 없음 빨강, 보유 초록 + 열쇠 아이콘). 위키는 보유 여부를 모르므로
 * 기본은 빨강 자물쇠. 마우스를 올리면 열쇠 이름 말풍선(CSS :hover).
 */
export function DoorMarker({ door, style, owned }: DoorMarkerProps) {
  const t = useT()
  const icon = mapAssetUrl('icons', owned ? 'door_with_key.png' : 'door_with_lock.png')
  const state = owned === undefined ? '' : owned ? ' is-owned' : ' is-missing'
  const label = doorLabel(door, t)
  return (
    <span className={`qc-map__door${state}`} style={{ ...style, ['--icon' as string]: `url("${icon}")` }}>
      <img src={icon} alt={label} draggable={false} />
      <span className="qc-map__tint" aria-hidden />
      <span className="qc-map__tip" role="tooltip">{label}</span>
    </span>
  )
}

/**
 * 탈출구·환승(10 스펙 §3.2): tarkov.dev 방패 아이콘(종류별 색이 그림에 들어 있다)만 그리고, 마우스를 올리면
 * 첫 줄 "이름 · 종류"(환승은 "환승 → 목적지"), 둘째 줄 "조건 · 등장 확률"(없으면 생략). 이름은 모든 언어에서 영문.
 * 탈출구는 층을 바꾸지 않아도 보이게 모든 층에 그리고, 다른 층(other)의 것은 흐리게 한다(벙커·엘리베이터 등).
 */
export function ExitMarker({ exit, style, other }: { exit: MapExit; style: CSSProperties; other: boolean }) {
  const t = useT()
  const icon = mapAssetUrl('icons', `extract_${exit.kind}.png`)
  const title = exit.kind === 'transit'
    ? t('map.exit.transitTo', { map: serverMapName(exit.target ?? '', t) })
    : `${exit.name} · ${t(`map.exit.kind.${exit.kind}` as UiKey)}`
  const requirement = exit.requirement ?? (exit.requirementKind ? t(`map.exit.req.${exit.requirementKind}` as UiKey) : null)
  const detail = [requirement, exit.chance != null ? t('map.exit.chance', { n: exit.chance }) : null].filter(Boolean).join(' · ')
  return (
    <span className={other ? 'qc-map__exit is-other' : 'qc-map__exit'} style={style}>
      <img src={icon} alt={title} draggable={false} />
      <span className="qc-map__tip" role="tooltip">
        <span>{title}</span>
        {detail && <span className="qc-map__tip-sub">{detail}</span>}
      </span>
    </span>
  )
}

/**
 * 보스 한 줄: "이름 (고레벨, 밤) 39%". 확률은 맵 어디서든 나올 값 하나만 쓴다.
 * 조건부는 "이름 · 조건부"로 숫자를 쓰지 않는다 — 합친 값은 모든 신호를 눌렀을 때의 확률이라 연구소 레이더가 100% 로 나온다.
 * 맵을 순환하는 보스는 "(순환)"을 붙인다 — 숫자는 이 맵에 배정됐을 때의 값이고, 지금 이 맵인지는 알 수 없다.
 * 광신도는 서버 표에 시간 조건이 없지만 클라이언트가 낮에 막는다(BotZonesLeaveController.DayBlocks). 야간 공장은 이미 "야간"이라 덧붙이지 않는다.
 */
export function bossLine(boss: TabBossEntry, t: T): string {
  const labels = [
    boss.onlyOn ? t(`map.boss.on.${boss.onlyOn}` as UiKey) : null,
    boss.key === 'sectantPriest' && boss.onlyOn !== 'factory4_night' ? t('map.boss.night') : null,
    boss.rotating ? t('map.boss.rotating') : null,
  ].filter(Boolean)
  const name = t(`map.boss.name.${boss.key}` as UiKey) + (labels.length > 0 ? ` (${labels.join(', ')})` : '')
  return [
    boss.conditional ? name : `${name} ${boss.mapChance}%`,
    boss.conditional ? t('map.boss.conditional') : null,
  ].filter(Boolean).join(' · ')
}

/**
 * 보스 출현 구역(18 스펙): 구역마다 마커 하나, 마우스를 올리면 그 구역의 보스와 확률 목록. 준보스만 있는 구역은 색을 달리한다.
 * 탈출구처럼 모든 층에 그리고 다른 층의 것은 흐리게 한다. 그림은 직접 그린 해골이라 라이선스 표기가 필요 없다.
 */
export function BossMarker({ zone, style, other }: { zone: TabBossZone; style: CSSProperties; other: boolean }) {
  const t = useT()
  const minor = zone.bosses.every((b) => b.kind === 'minor')
  return (
    <span className={`qc-map__boss${minor ? ' is-minor' : ''}${other ? ' is-other' : ''}`} style={style}>
      <svg viewBox="0 0 16 16" aria-hidden>
        <path d="M8 1.5c-3.3 0-5.5 2.2-5.5 5 0 1.8.9 3.2 2.3 4.1V13c0 .6.4 1 1 1h4.4c.6 0 1-.4 1-1v-2.4c1.4-.9 2.3-2.3 2.3-4.1 0-2.8-2.2-5-5.5-5Z" fill="currentColor" />
        <circle cx="5.8" cy="7" r="1.5" fill="var(--boss-eye)" />
        <circle cx="10.2" cy="7" r="1.5" fill="var(--boss-eye)" />
        <path d="M8 9.2 7.1 10.8h1.8Z" fill="var(--boss-eye)" />
      </svg>
      <span className="qc-map__tip" role="tooltip">
        {zone.bosses.map((b) => <span key={`${b.key}|${b.conditional}|${b.onlyOn ?? ''}`}>{bossLine(b, t)}</span>)}
      </span>
    </span>
  )
}

/** 정해진 구역이 없는 보스(Partisan, 미궁 등) — 지도에 찍을 곳이 없어 왼쪽 아래에 목록으로 둔다. 드래그를 막지 않게 CSS 로 포인터를 통과시킨다. */
export function BossUnplaced({ bosses }: { bosses: TabBosses['unplaced'] }) {
  const t = useT()
  if (bosses.length === 0) return null
  return (
    <div className="qc-map__free">
      <span className="qc-map__free-h">{t('map.boss.unplaced')}</span>
      {bosses.map((b) => <span key={`${b.key}|${b.conditional}|${b.onlyOn ?? ''}`}>{bossLine(b, t)}</span>)}
    </div>
  )
}
