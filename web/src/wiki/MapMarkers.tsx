import type { CSSProperties } from 'react'
import type { LockedDoor, MapExit } from '../api/catalog'
import { useT } from '../i18n/I18nContext'
import type { UiKey } from '../i18n/index'
import { mapAssetUrl } from './mapAssets'
import { serverMapName } from './mapNames'

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
  const label = door.kind === 'keycard' ? `${t('map.keycard')} · ${door.keyName}` : door.keyName
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
