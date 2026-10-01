import { Fragment, useMemo, useState } from 'react'
import type { Catalog } from '../api/catalog'
import type { ProfileProgress } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { assignModColors, type NameLookup } from '../wiki/derive'
import { formatInt } from '../wiki/format'
import { CategoryBar, countBy } from './CategoryBar'
import { lockReasonText, statusLabel } from './format'
import { QuestLink } from './parts'
import { filterUnlockRows, nextStep, UNLOCK_CHIPS, unlockCounts, unlockRows, type UnlockChip, type UnlockRow } from './unlock'
import { ModTag, UnlockDetail } from './UnlockDetail'
import { UnlockPathDialog } from './UnlockPathDialog'
import { blockText, sourceLabel } from './unlockText'

interface UnlocksViewProps {
  catalog: Catalog
  progress: ProfileProgress
  lookup: NameLookup
}

/** 해금 경로(G1~G5) — 판매 해금·제작법 아이템마다 가장 가까운 입수처와 남은 퀘스트. 필요 아이템과 같은 틀(칩·검색·카테고리·표). */
export function UnlocksView({ catalog, progress, lookup }: UnlocksViewProps) {
  const t = useT()
  const [chip, setChip] = useState<UnlockChip>('all')
  const [query, setQuery] = useState('')
  const [picked, setCategory] = useState<string | null>(null)
  /** 행 → 펼친 입수처 index. null = 사용자가 접음(검색 자동 펼침보다 우선) */
  const [opened, setOpened] = useState<ReadonlyMap<string, number | null>>(() => new Map())
  const [dialog, setDialog] = useState<{ tpl: string; index: number } | null>(null)
  const rows = useMemo(() => unlockRows(catalog, progress), [catalog, progress])
  const modColors = useMemo(() => assignModColors(Object.values(catalog.quests)), [catalog])
  // 칩 개수는 고른 카테고리 안에서, 카테고리 개수는 고른 칩 안에서(검색어는 둘 다 무시) — 필요 아이템과 같은 규칙
  const category = picked !== null && rows.some((r) => r.category === picked) ? picked : null
  const counts = useMemo(() => unlockCounts(rows, category), [rows, category])
  const present = useMemo(() => new Set(rows.map((r) => r.category)), [rows])
  const categoryCounts = useMemo(() => countBy(filterUnlockRows(rows, chip, ''), (r) => r.category), [rows, chip])
  const visible = useMemo(() => filterUnlockRows(rows, chip, query, category), [rows, chip, query, category])
  // 검색 결과가 1~2개면 첫 행을 펼친다(목업 ②)
  const auto = query.trim() !== '' && visible.length <= 2
  const openOf = (tpl: string, i: number) => (opened.has(tpl) ? opened.get(tpl)! : auto && i === 0 ? 0 : null)
  const setOpen = (tpl: string, index: number | null) => setOpened((prev) => new Map(prev).set(tpl, index))
  const dialogRow = dialog ? rows.find((r) => r.tpl === dialog.tpl) : undefined
  const dialogPlan = dialog && dialogRow ? dialogRow.plans[dialog.index] ?? null : null

  return (
    <section className="qc-card qc-card--flush qc-items qc-unlock">
      <div className="qc-card__bar">
        <div className="qc-chips" role="group" aria-label={t('filter.label')}>
          {UNLOCK_CHIPS.map((c) => (
            <button key={c} type="button" className={cls('qc-chip', c === 'unreachable' && 'qc-chip--bad', chip === c && 'is-on')} aria-pressed={chip === c} onClick={() => setChip(c)}>
              {t(`unlock.chip.${c}`)} {formatInt(counts[c])}
            </button>
          ))}
        </div>
        <input className="qc-search" type="search" placeholder={t('unlock.search')} aria-label={t('unlock.search')} value={query} onChange={(e) => setQuery(e.target.value)} />
      </div>
      <CategoryBar categories={catalog.itemCategories} present={present} counts={categoryCounts} value={category} onChange={setCategory} />
      {visible.length === 0 ? <p className="qc-empty">{t('unlock.empty')}</p> : (
        <table className="qc-table">
          <thead>
            <tr>
              <th>{t('unlock.col.item')}</th>
              <th>{t('unlock.col.source')}</th>
              <th>{t('unlock.col.now')}</th>
              <th className="qc-num">{t('unlock.col.remaining')}</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((r, i) => {
              const open = openOf(r.tpl, i)
              return (
                <Fragment key={r.tpl}>
                  <UnlockTableRow row={r} catalog={catalog} progress={progress} lookup={lookup} modColors={modColors} open={open !== null}
                    onToggle={() => setOpen(r.tpl, open === null ? 0 : null)} />
                  {open !== null && (
                    <tr className="qc-table__detail">
                      <td colSpan={4}>
                        <UnlockDetail row={r} catalog={catalog} progress={progress} lookup={lookup} modColors={modColors} open={open}
                          onOpen={(index) => setOpen(r.tpl, index)} onShowPath={(index) => setDialog({ tpl: r.tpl, index })} />
                      </td>
                    </tr>
                  )}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      )}
      <UnlockPathDialog row={dialogRow ?? null} plan={dialogPlan} catalog={catalog} progress={progress} lookup={lookup} onClose={() => setDialog(null)} />
    </section>
  )
}

interface RowProps {
  row: UnlockRow
  catalog: Catalog
  progress: ProfileProgress
  lookup: NameLookup
  modColors: Record<string, number>
  open: boolean
  onToggle(): void
}

function UnlockTableRow({ row, catalog, progress, lookup, modColors, open, onToggle }: RowProps) {
  const t = useT()
  const shown = row.best ?? row.plans.find((p) => p.state === 'unlocked' && p.source.kind === 'sale') ?? row.plans.find((p) => p.state === 'unlocked') ?? row.plans[0]
  const quest = catalog.quests[shown.source.questId]
  const now = row.best && nextStep(row.best)
  const firstLock = row.best && !now ? progress.quests[row.best.steps[0]?.questId]?.lockReasons[0] : undefined
  const more = row.plans.filter((p) => p.state !== 'unlocked').length - 1
  return (
    <tr className={cls('qc-table__row', open && 'is-open')}>
      <td>
        <button type="button" className="qc-items__toggle" aria-expanded={open} onClick={onToggle}>
          <span className="qc-row__caret">{open ? '▾' : '▸'}</span> {row.name}
        </button>
        {row.craftUnlocked && <> <span className="qc-tag qc-tag--ok" title={t('unlock.craftDoneHint')}>{t('unlock.craftDone')}</span></>}
        {quest && !quest.isVanilla && <> <ModTag quest={quest} colors={modColors} /></>}
      </td>
      <td className="qc-items__quests">
        {row.state === 'unlocked'
          ? t('unlock.via', { source: sourceLabel(shown.source, lookup, t), quest: quest?.name ?? '' })
          : sourceLabel(shown.source, lookup, t)}
        {row.state !== 'unlocked' && more > 0 && <span className="qc-muted"> {t('unlock.moreSources', { n: more })}</span>}
      </td>
      {row.state === 'unreachable' && shown.blockReason ? (
        <td colSpan={2} className="qc-bad">{blockText(shown.blockReason, catalog, t)}</td>
      ) : row.best ? (
        <>
          <td>
            {now && catalog.quests[now.questId] && <><QuestLink quest={catalog.quests[now.questId]} /> <span className="qc-muted">{statusLabel(now.status, t)}</span></>}
            {firstLock && <span className="qc-muted">{lockReasonText(firstLock, lookup, t)}</span>}
            {row.best.maxLevel !== null && row.best.maxLevel > progress.level && <> <span className="qc-warn">{t('unlock.levelNeed', { n: row.best.maxLevel })}</span></>}
          </td>
          <td className="qc-num">{formatInt(row.best.remaining)}</td>
        </>
      ) : (
        <><td className="qc-muted">—</td><td className="qc-num qc-muted">—</td></>
      )}
    </tr>
  )
}
