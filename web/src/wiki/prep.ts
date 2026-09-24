import type { CatalogQuest, ItemRef, Objective, ObjectivePrep, PrepItem } from '../api/catalog'
import type { T, UiKey } from '../i18n/index'

// 준비물 팝업·목표 칩의 표시 문자열. format.ts 와 같은 규칙 — JSX 없이 t 를 받아 React 없이 테스트된다.

const EXIT_STATUSES = new Set(['Survived', 'Runner', 'Transit', 'Killed', 'Left', 'MissingInAction'])

/**
 * 준비물이 있는 목표인가. `!= null` 인 이유: prep 필드가 생기기 전 서버(구버전 모드 DLL + 새 프론트, 예: vite dev)는
 * 필드 자체가 없어 undefined 가 온다 — `!== null` 로 보면 준비물이 있는 것으로 세고 팝업에서 터진다.
 */
export function hasPrep(o: Objective): o is Objective & { prep: ObjectivePrep } {
  return o.prep != null
}

/** 버튼 배지: 준비물이 있는 목표 수 */
export function prepCount(q: CatalogQuest): number {
  return q.objectives.filter(hasPrep).length
}

/** 팝업 머리의 맵 목록. 퀘스트에 묶인 맵이 먼저, 목표별 맵이 뒤. */
export function questMaps(q: CatalogQuest): string[] {
  const maps = q.location ? [q.location] : []
  for (const o of q.objectives) {
    for (const m of o.prep?.maps ?? []) if (!maps.includes(m)) maps.push(m)
  }
  return maps
}

/**
 * 팝업 머리의 맵 표기. 여러 맵이면 나열하지 않는다 — 목표별 섹션에 맵이 따로 나오고, 호위·안내자처럼 8~12개를
 * 한 줄에 늘어놓으면 읽을 수 없다. 퀘스트 맵 하나만 고르지도 않는다: 멀티맵 퀘스트의 location 은 그중 하나일 뿐이라
 * "맵: 공장" 이 틀린 인상을 준다. 맵이 하나면 그대로 — 목표에 맵 조건이 없는 퀘스트는 이게 유일한 맵 정보다.
 */
export function headerMap(q: CatalogQuest, t: T): string | null {
  const maps = questMaps(q)
  if (maps.length === 0) return null
  return maps.length === 1 ? `${t('prep.map')}: ${maps[0]}` : t('prep.multiMap')
}

const hasWeaponRule = (p: ObjectivePrep) => p.weapons.length > 0 || p.calibers.length > 0 || p.weaponMods.length > 0
const hasGearRule = (p: ObjectivePrep) => p.equipment.length > 0 || p.forbiddenEquipment.length > 0
const hasExitRule = (p: ObjectivePrep) => p.exitStatuses.length > 0 || p.exitName !== null

/** 팝업에서 목표별 섹션이 필요한가 — 아이템만 있는 목표는 "챙길 아이템" 에만 나온다. */
export function hasRules(p: ObjectivePrep): boolean {
  return p.maps.length > 0 || hasWeaponRule(p) || hasGearRule(p) || p.oneRaid || hasExitRule(p)
}

/** 아이템 줄 뒤에 붙는 제한 조건. FIR 은 칩으로 따로 그린다. */
export function itemDetails(item: PrepItem, t: T): string[] {
  const out: string[] = []
  const { minDurability: min, maxDurability: max } = item
  if (min !== null && max !== null) out.push(t('prep.durRange', { min, max }))
  else if (min !== null) out.push(t('prep.durMin', { min }))
  else if (max !== null) out.push(t('prep.durMax', { max }))
  if (item.dogtagLevel !== null) out.push(t('prep.dogtag', { n: item.dogtagLevel }))
  if (item.plantSeconds !== null) out.push(t('prep.plantTime', { n: item.plantSeconds }))
  return out
}

/**
 * 표시 이름 기준 중복 제거(순서 유지). tpl 은 달라도 이름이 같은 변종이 흔하다 — 인식표의 TUE/EOD/프레스티지
 * 변종은 전부 "Dogtag BEAR" 이고, 데이터 목록을 그대로 그리면 같은 이름이 반복돼 보인다.
 */
export function distinctNames(names: string[]): string[] {
  return [...new Set(names)]
}

/** AND 묶음 하나 (대부분 아이템 1개) */
export function optionText(option: ItemRef[]): string {
  return option.map((i) => i.name).join(' + ')
}

export function exitText(p: ObjectivePrep, t: T): string | null {
  if (!hasExitRule(p)) return null
  const parts = p.exitStatuses.map((s) => (EXIT_STATUSES.has(s) ? t(`prep.exitStatus.${s}` as UiKey) : s))
  if (p.exitName !== null) parts.push(t('prep.exitVia', { name: p.exitName }))
  return parts.join(' · ')
}
