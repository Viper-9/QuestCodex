import { Fragment, useMemo, useState } from 'react'
import type { Catalog } from '../api/catalog'
import type { Holding, ProfileProgress } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import type { NameLookup } from '../wiki/derive'
import { formatInt } from '../wiki/format'
import {
  aggregateNeeds, filterNeedRows, ITEM_FILTERS, itemNeeds, mergeSources, missing, rowCategory, sortNeedRows, type ItemFilter, type NeedRow,
} from './derive'
import { CategoryBar, countBy } from './CategoryBar'
import { ItemName, QuestLink } from './parts'

interface ItemsViewProps {
  catalog: Catalog
  progress: ProfileProgress
  inventory: Record<string, Holding>
  lookup: NameLookup
}

/** 필요 아이템(I1·I2) — 끝나지 않은 퀘스트가 요구하는 아이템을 합산해 보유와 비교. 행을 펼치면 퀘스트별 내역. */
export function ItemsView({ catalog, progress, inventory }: ItemsViewProps) {
  const t = useT()
  const [filter, setFilter] = useState<ItemFilter>('all')
  const [query, setQuery] = useState('')
  const [picked, setCategory] = useState<string | null>(null)
  const categoryOf = catalog.itemCategoryOf
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  const rows = useMemo(
    () => sortNeedRows(aggregateNeeds(itemNeeds(catalog, progress), inventory)),
    [catalog, progress, inventory],
  )
  // 필터 칩 개수는 고른 카테고리 안에서, 카테고리 개수는 고른 필터 안에서 센다(검색어는 둘 다 무시).
  // 프로필을 바꿔 고른 카테고리의 행이 없어지면 전체로 본다.
  const category = picked !== null && rows.some((r) => rowCategory(r, categoryOf) === picked) ? picked : null
  const counts = useMemo(
    () => Object.fromEntries(ITEM_FILTERS.map((f) => [f, filterNeedRows(rows, f, '', category, categoryOf).length])) as Record<ItemFilter, number>,
    [rows, category, categoryOf],
  )
  const present = useMemo(() => new Set(rows.map((r) => rowCategory(r, categoryOf))), [rows, categoryOf])
  const categoryCounts = useMemo(
    () => countBy(filterNeedRows(rows, filter, ''), (r) => rowCategory(r, categoryOf)),
    [rows, filter, categoryOf],
  )
  const visible = useMemo(() => filterNeedRows(rows, filter, query, category, categoryOf), [rows, filter, query, category, categoryOf])
  const toggle = (key: string) => setExpanded((prev) => {
    const next = new Set(prev)
    if (!next.delete(key)) next.add(key)
    return next
  })

  return (
    <section className="qc-card qc-card--flush qc-items">
      <div className="qc-card__bar">
        <div className="qc-chips" role="group" aria-label={t('filter.label')}>
          {ITEM_FILTERS.map((f) => (
            <button key={f} type="button" className={cls('qc-chip', filter === f && 'is-on')} aria-pressed={filter === f} onClick={() => setFilter(f)}>
              {t(`items.filter.${f}`)} {formatInt(counts[f])}
            </button>
          ))}
        </div>
        <input
          className="qc-search"
          type="search"
          placeholder={t('items.search')}
          aria-label={t('items.search')}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </div>
      <CategoryBar categories={catalog.itemCategories} present={present} counts={categoryCounts} value={category} onChange={setCategory} />
      {visible.length === 0 ? <p className="qc-empty">{t('items.empty')}</p> : (
        <table className="qc-table">
          <thead>
            <tr>
              <th>{t('items.col.item')}</th>
              <th className="qc-num">{t('items.col.need')}</th>
              <th className="qc-num">{t('items.col.needFir')}</th>
              <th className="qc-num">{t('items.col.have')}</th>
              <th className="qc-num">{t('items.col.fir')}</th>
              <th className="qc-num">{t('items.col.short')}</th>
              <th>{t('items.col.quests')}</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((r) => (
              <ItemRow key={r.key} row={r} catalog={catalog} open={expanded.has(r.key)} onToggle={() => toggle(r.key)} />
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

/** FIR 칸처럼 0 이 흔한 숫자는 흐린 — 로 */
function OptionalNum({ n }: { n: number }) {
  return <td className={cls('qc-num', n === 0 && 'qc-muted')}>{n > 0 ? formatInt(n) : '—'}</td>
}

function ItemRow({ row, catalog, open, onToggle }: { row: NeedRow; catalog: Catalog; open: boolean; onToggle(): void }) {
  const t = useT()
  const short = missing(row)
  const questIds = [...new Set(row.sources.map((s) => s.questId))]
  const firstQuest = catalog.quests[questIds[0]]
  return (
    <Fragment>
      <tr className={cls('qc-table__row', open && 'is-open')}>
        <td>
          <button type="button" className="qc-items__toggle" aria-expanded={open} onClick={onToggle}>
            <span className="qc-row__caret">{open ? '▾' : '▸'}</span> <ItemName items={row.items} />
          </button>
        </td>
        <td className="qc-num">{formatInt(row.need)}</td>
        <OptionalNum n={row.needFir} />
        <td className="qc-num">{formatInt(row.have)}</td>
        <OptionalNum n={row.haveFir} />
        <td className={cls('qc-num', short > 0 ? 'qc-warn' : 'qc-muted')}>{short > 0 ? formatInt(short) : '—'}</td>
        <td className="qc-items__quests">
          {questIds.length === 1 ? firstQuest?.name : t('items.moreQuests', { name: firstQuest?.name ?? '', n: questIds.length - 1 })}
        </td>
      </tr>
      {open && (
        <tr className="qc-table__detail">
          <td colSpan={7}>
            <ul className="qc-lines">
              {mergeSources(row.sources).map((s) => {
                const q = catalog.quests[s.questId]
                return (
                  <li key={s.conditionId}>
                    {q && <QuestLink quest={q} />}
                    <span className="qc-muted">
                      {' · '}{s.action === 'plant' ? t('prep.plant') : t('prep.handover')} ×{formatInt(s.count)}
                    </span>
                    {s.fir && <span className="qc-prep__fir" title={t('prep.firHint')}>{t('prep.fir')}</span>}
                  </li>
                )
              })}
            </ul>
          </td>
        </tr>
      )}
    </Fragment>
  )
}
