import { useMemo, useState } from 'react'
import type { Catalog, ObjectivePrep } from '../api/catalog'
import type { Holding, ProfileProgress } from '../api/progress'
import { cls } from '../cls'
import type { T, UiKey } from '../i18n/index'
import { useT } from '../i18n/I18nContext'
import { navigate } from '../shell/router'
import type { NameLookup } from '../wiki/derive'
import { formatInt, formatObjective, lineText } from '../wiki/format'
import { distinctNames, exitText, optionText } from '../wiki/prep'
import { objectiveColor } from '../wiki/mapProjection'
import { dedupeRows, entryPlace, groupByQuest, MAP_ORDER, orderRaidQuests, mapBrief, mapTabs, missing, placeFinds, raidEntries, raidFinds, raidMapPlan, type NeedRow, type PlacedRow, type RuleRow, type RaidEntry } from './derive'
import { Counter, ItemName, QuestLink } from './parts'
import { RaidMap } from './RaidMap'

interface RaidViewProps {
  catalog: Catalog
  progress: ProfileProgress
  inventory: Record<string, Holding>
  lookup: NameLookup
  /** ?map= — 없거나 모르는 값이면 할 일이 있는 첫 맵 */
  map: string | null
}

/** 번역이 있는 지도 변형(09 스펙) — i18n 키는 map.name.<맵 키>-<변형 ID>, 위치정보 팝업 탭 이름(변형 폴더 키)과 같다 */
const VARIANT_NAMES = new Set(['interchange-manimal'])

/**
 * 탭 이름. 번역이 없는 맵(모드 맵)은 키 그대로 — feature/quest-map 의 mapName 과 같은 규칙.
 * 맵 교체 모드가 로드된 서버(catalog.mapVariants)면 변형 이름("확장된 인터체인지").
 */
function mapName(key: string, t: T, variants: Record<string, string> | undefined): string {
  const variant = variants?.[key] && `${key}-${variants[key]}`
  if (variant && VARIANT_NAMES.has(variant)) return t(`map.name.${variant}` as UiKey)
  return MAP_ORDER.includes(key) ? t(`map.name.${key}` as UiKey) : key
}

/** 레이드 준비 — 맵 브리핑(B1~B6) + 위치 지도. 지도 번호는 퀘스트 단위라 "이 맵" 목록 줄에도 같은 번호를 붙인다. */
export function RaidView({ catalog, progress, inventory, lookup, map }: RaidViewProps) {
  const t = useT()
  const entries = useMemo(() => raidEntries(catalog, progress), [catalog, progress])
  const tabs = useMemo(() => mapTabs(entries), [entries])
  const selected = tabs.some((x) => x.key === map) ? map! : (tabs.find((x) => x.count > 0) ?? tabs[0]).key
  const brief = useMemo(() => mapBrief(entries, selected, inventory), [entries, selected, inventory])
  const needs = useMemo(() => raidFinds(catalog, progress, inventory), [catalog, progress, inventory])
  const finds = useMemo(() => placeFinds(needs, catalog, selected), [needs, catalog, selected])
  const plan = useMemo(() => raidMapPlan(brief.here, selected), [brief, selected])
  const [hot, setHot] = useState<number | null>(null)

  return (
    <div className="qc-raid">
      <div className="qc-maps" role="group" aria-label={t('raid.maps')}>
        {tabs.map((x) => (
          <button
            key={x.key}
            type="button"
            className={cls('qc-map', x.key === selected && 'is-on', x.count === 0 && 'is-empty')}
            aria-pressed={x.key === selected}
            onClick={() => { setHot(null); navigate('progress', 'raid', { map: x.key }) }}
          >
            {mapName(x.key, t, catalog.mapVariants)} <span className="qc-map__n">{x.count}</span>
          </button>
        ))}
      </div>

      <div className="qc-raid__grid">
        {/* 이 맵 / 모든 맵을 카드로 나눈다 — 한 카드 안 소제목으로는 경계가 잘 안 보인다는 피드백 */}
        <div className="qc-raid__main">
          <section className="qc-card">
            <h3 className="qc-card__h">{t('raid.todo', { map: mapName(selected, t, catalog.mapVariants) })}</h3>
            {brief.here.length === 0 && <p className="qc-muted">{t('raid.nothing')}</p>}
            <QuestGroups entries={brief.here} all={entries} map={selected} lookup={lookup} numbers={plan.numbers} hot={hot} onHot={setHot} />
          </section>
          {brief.anywhere.length > 0 && (
            <section className="qc-card">
              <h3 className="qc-card__h">{t('raid.anywhere')}</h3>
              <QuestGroups entries={brief.anywhere} all={entries} map={selected} lookup={lookup} />
            </section>
          )}
          {/* 오른쪽 칸이 길어 왼쪽 아래가 비므로 거기에 두고, 스크롤해도 따라오게 sticky(progress.css) */}
          <RaidMap
            map={selected} mapLabel={mapName(selected, t, catalog.mapVariants)} plan={plan} lockedDoors={catalog.lockedDoors} mapExits={catalog.exits}
            mapVariants={catalog.mapVariants} hot={hot} onHot={setHot}
          />
        </div>

        <div className="qc-raid__side">
          <section className="qc-card">
            <h3 className="qc-card__h">{t('raid.bring')}</h3>
            {brief.bring.length === 0 ? <p className="qc-muted">{t('overview.nothing')}</p> : (
              <ul className="qc-bring">{brief.bring.map((r) => <BringRow key={r.key} row={r} />)}</ul>
            )}
            {brief.bringAnywhere.length > 0 && (
              <>
                <h4 className="qc-group__h qc-raid__any">{t('raid.anywhere')}</h4>
                <ul className="qc-bring qc-bring--any">{brief.bringAnywhere.map((r) => <BringRow key={r.key} row={r} />)}</ul>
              </>
            )}
          </section>

          <section className="qc-card">
            <h3 className="qc-card__h">{t('raid.find')}</h3>
            <FindGroups finds={finds} catalog={catalog} mapLabel={t('raid.findMap', { map: mapName(selected, t, catalog.mapVariants) })} />
          </section>

          <section className="qc-card">
            <h3 className="qc-card__h">{t('raid.gear')}</h3>
            {brief.gear.length === 0 ? <p className="qc-muted">{t('overview.nothing')}</p> : (
              <QuestRules entries={brief.gear} rowsOf={gearRows} warnLabel={t('prep.forbidden')} />
            )}
          </section>

          <section className="qc-card">
            <h3 className="qc-card__h">{t('raid.special')}</h3>
            {brief.special.length === 0 ? <p className="qc-muted">{t('overview.nothing')}</p> : (
              <QuestRules entries={brief.special} rowsOf={specialRows} />
            )}
          </section>
        </div>
      </div>
    </div>
  )
}

/**
 * 퀘스트 한 줄 + 펼치면 이 칸에 해당하는 목표들(현황의 퀘스트 줄과 같은 모양). 목표가 하나면 줄에 바로 그 목표와 카운터,
 * 여럿이면 "목표 N개" — 한 퀘스트의 목표 4~5개가 줄줄이 나와 읽기 힘들다는 피드백.
 */
interface QuestGroupsProps {
  /** 이 칸(이 맵 / 맵 미상)에 나오는 목표 — 줄 요약과 "목표 N개" 기준 */
  entries: RaidEntry[]
  /** 모든 맵의 레이드 목표 — 펼치면 그 퀘스트의 목표를 전부 보여 주고, 지금 탭 기준으로 강조·흐림 */
  all: RaidEntry[]
  map: string
  lookup: NameLookup
  /** 지도 번호(퀘스트 → 번호). 주면 퀘스트 이름 뒤에 지도 마커와 같은 색 번호를 붙이고, 마우스를 올리면 지도에서 강조한다. */
  numbers?: Map<string, number>
  hot?: number | null
  onHot?(n: number | null): void
}

function QuestGroups({ entries, all, map, lookup, numbers, hot, onHot }: QuestGroupsProps) {
  const t = useT()
  const [open, setOpen] = useState<ReadonlySet<string>>(() => new Set())
  const toggle = (id: string) => setOpen((prev) => {
    const next = new Set(prev)
    if (!next.delete(id)) next.add(id)
    return next
  })
  return (
    <ul className="qc-rgroups">
      {orderRaidQuests(entries, map).map(({ quest, entries: list }) => {
        const isOpen = open.has(quest.id)
        const single = list.length === 1 ? list[0] : null
        const n = numbers?.get(quest.id)
        return (
          <li
            key={quest.id}
            className={cls('qc-pline', isOpen && 'is-open', n !== undefined && n === hot && 'is-hot')}
            onMouseEnter={n !== undefined ? () => onHot?.(n) : undefined}
            onMouseLeave={n !== undefined ? () => onHot?.(null) : undefined}
          >
            <button type="button" className="qc-pline__row qc-rgroup__row" aria-expanded={isOpen} onClick={() => toggle(quest.id)}>
              <span className="qc-pline__name">
                {quest.name}
                {n !== undefined && <span className="qc-rgroup__num" style={{ ['--c' as string]: objectiveColor(n) }}>{n}</span>}
              </span>
              <span className="qc-pline__trader">{lookup.traderName(quest.traderId)}</span>
              <span className="qc-pline__summary">
                {single ? lineText(formatObjective(single.objective, t)) : t('raid.objCount', { n: list.length })}
              </span>
              <span className="qc-pline__counter">{single && <Counter op={single.progress} />}</span>
            </button>
            {isOpen && (
              <div className="qc-pline__detail">
                <ul className="qc-lines">
                  {all.filter((e) => e.quest.id === quest.id).map((e) => (
                    <li key={e.objective.conditionId} className={`qc-place--${entryPlace(e, map)}`}>
                      <span className="qc-pline__obj">
                        {lineText(formatObjective(e.objective, t))}
                        {e.inferred && <span className="qc-tag qc-inferred" title={t('raid.inferredHint')}>{t('raid.inferred')}</span>}
                      </span>
                      <span className="qc-pline__counter"><Counter op={e.progress} /></span>
                    </li>
                  ))}
                </ul>
                <button type="button" className="qc-btn" onClick={() => navigate('wiki', null, { quest: quest.id })}>{t('overview.openWiki')}</button>
              </div>
            )}
          </li>
        )
      })}
    </ul>
  )
}

/**
 * 아이템 줄의 FIR 칸 — FIR 이 아닌 줄도 칸을 비워 둔다. 목록(.qc-bring)이 [이름 | FIR | 수량] 3열 그리드라
 * FIR 표시가 세로로 한 줄에 서고 수량은 늘 오른쪽 끝에 붙는다(배지가 일부 줄에만 붙어 수량이 들쭉날쭉하다는 피드백).
 */
function FirSlot({ fir }: { fir: boolean }) {
  const t = useT()
  return <span className="qc-bring__fir">{fir && <span className="qc-fir" title={t('prep.firHint')}>{t('prep.fir')}</span>}</span>
}

function BringRow({ row }: { row: NeedRow }) {
  const t = useT()
  const short = missing(row)
  return (
    <li className={cls('qc-bring__row', short > 0 && 'is-short')}>
      <ItemName items={row.items} />
      <FirSlot fir={row.needFir > 0} />
      <span className="qc-bring__num">
        {t('raid.needHave', { need: formatInt(row.need), have: formatInt(row.needFir > 0 ? row.haveFir : row.have) })}
        {short > 0 ? <span className="qc-warn"> ⚠</span> : <span className="qc-ok"> ✓</span>}
      </span>
    </li>
  )
}

/**
 * 파밍할 아이템을 고른 맵 / 모든 맵 소제목으로 나눈다. 다른 맵에서 구하는 것은 이번 레이드와 무관해 그리지 않는다(피드백).
 * 고른 맵 묶음은 비어도 제목과 "없음" 을 그린다 — 맵을 바꿔 가며 볼 때 "이 맵엔 없다" 가 보여야 한다. 모든 맵은 비면 생략.
 */
function FindGroups({ finds, catalog, mapLabel }: { finds: PlacedRow[]; catalog: Catalog; mapLabel: string }) {
  const t = useT()
  return (
    <>
      {(['here', 'unknown'] as const).map((place) => {
        const rows = finds.filter((p) => p.place === place)
        if (rows.length === 0 && place !== 'here') return null
        return (
          <div key={place} className={cls('qc-findgroup', `qc-place--${place}`)}>
            <h4 className="qc-group__h">{place === 'here' ? mapLabel : t('raid.anywhere')}</h4>
            {rows.length === 0
              ? <p className="qc-muted qc-find__none">{t('raid.findNone')}</p>
              : <ul className="qc-bring">{rows.map((p) => <FindRow key={p.row.key} placed={p} catalog={catalog} />)}</ul>}
          </div>
        )
      })}
    </>
  )
}

/**
 * 파밍할 아이템 한 줄: 이름 · 부족 수(+FIR). 요구 퀘스트는 줄에 쓰지 않고 툴팁으로 —
 * 아이템 밑에 퀘스트 줄을 두면 한 항목이 두 줄이 되어 목록이 읽기 어렵다(피드백).
 */
function FindRow({ placed, catalog }: { placed: PlacedRow; catalog: Catalog }) {
  const t = useT()
  const { row } = placed
  const quests = [...new Set(row.sources.map((s) => catalog.quests[s.questId]?.name ?? s.questId))]
  return (
    <li className="qc-bring__row qc-find" title={quests.join('\n')}>
      <ItemName items={row.items} />
      <FirSlot fir={row.needFir > 0} />
      <span className="qc-bring__num qc-warn">{t('items.short', { n: formatInt(missing(row)) })}</span>
    </li>
  )
}

/**
 * 대안 목록. 하나면 이름 그대로, 여럿이면 첫 이름 + "외 N종" 버튼 → 누르면 한 줄에 하나씩 전부 펼친다.
 * (위키 가이드 팝업은 앞 3개를 보여 주지만, 브리핑은 여러 목표가 한 칸에 모여서 한 줄로 접는다.)
 */
function Expandable({ names: raw }: { names: string[] }) {
  const t = useT()
  const [open, setOpen] = useState(false)
  const names = distinctNames(raw)
  if (names.length <= 1) return <>{names[0]}</>
  if (!open) {
    return (
      <>
        {names[0]}{' '}
        <button type="button" className="qc-prep__more" aria-expanded={false} onClick={() => setOpen(true)}>
          {t('prep.more', { n: names.length - 1 })}
        </button>
      </>
    )
  }
  return (
    <ul className="qc-prep__names">
      {names.map((n, i) => <li key={i}>{n}</li>)}
      <li>
        <button type="button" className="qc-prep__more is-open" aria-expanded onClick={() => setOpen(false)}>{t('prep.less')}</button>
      </li>
    </ul>
  )
}

function gearRows(prep: ObjectivePrep, t: T): RuleRow[] {
  const rows: RuleRow[] = []
  if (prep.weapons.length > 0) rows.push({ label: t('prep.weapon'), names: prep.weapons.map((w) => w.name) })
  if (prep.calibers.length > 0) rows.push({ label: t('prep.caliber'), names: prep.calibers })
  if (prep.weaponMods.length > 0) rows.push({ label: t('prep.weaponMods'), names: prep.weaponMods.map(optionText) })
  // 슬롯끼리는 AND — 슬롯마다 한 줄. 줄 안의 대안은 OR.
  prep.equipment.forEach((slot) => rows.push({ label: t('prep.wear'), names: slot.map(optionText) }))
  if (prep.forbiddenEquipment.length > 0) rows.push({ label: t('prep.forbidden'), names: prep.forbiddenEquipment.map((i) => i.name) })
  return rows
}

function specialRows(prep: ObjectivePrep, t: T): RuleRow[] {
  const rows: RuleRow[] = []
  if (prep.oneRaid) rows.push({ label: t('prep.raid'), names: [t('prep.oneRaid')] })
  const exit = exitText(prep, t)
  if (exit !== null) rows.push({ label: t('prep.exit'), names: [exit] })
  return rows
}

interface QuestRulesProps {
  entries: RaidEntry[]
  rowsOf(prep: ObjectivePrep, t: T): RuleRow[]
  /** 이 라벨의 줄은 경고색(착용 금지) */
  warnLabel?: string
}

/** 장비·특수 조건을 퀘스트별로 한 덩어리 — 퀘스트 이름 아래에 그 퀘스트 목표들의 조건 줄을 모으고 같은 줄은 합친다. */
function QuestRules({ entries, rowsOf, warnLabel }: QuestRulesProps) {
  const t = useT()
  return (
    <ul className="qc-rules">
      {groupByQuest(entries).map(({ quest, entries: list }) => (
        <li key={quest.id}>
          <p className="qc-rules__src"><QuestLink quest={quest} /></p>
          <dl className="qc-rules__dl">
            {dedupeRows(list.flatMap((e) => rowsOf(e.objective.prep!, t))).map((r, i) => (
              <div key={i} className="qc-rules__row">
                <dt className={cls(r.label === warnLabel && 'qc-warn')}>{r.label}</dt>
                <dd><Expandable names={r.names} /></dd>
              </div>
            ))}
          </dl>
        </li>
      ))}
    </ul>
  )
}
