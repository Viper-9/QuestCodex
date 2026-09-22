import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import type { ChipKey, Chips, SortKey } from './derive'

interface FilterBarProps {
  query: string
  onQueryChange(query: string): void
  chips: Chips
  onToggleChip(key: ChipKey): void
  sort: SortKey
  onSortChange(sort: SortKey): void
}

// 라벨 키는 `filter.${key}` — en.json/kr.json 의 filter.vanilla / filter.mod / filter.bear / filter.usec
const CHIP_KEYS: readonly ChipKey[] = ['vanilla', 'mod', 'bear', 'usec']
// 라벨 키는 `sort.${key}` — en.json/kr.json 의 sort.level / sort.chain / sort.name
const SORT_KEYS: readonly SortKey[] = ['level', 'chain', 'name']

export function FilterBar({ query, onQueryChange, chips, onToggleChip, sort, onSortChange }: FilterBarProps) {
  const t = useT()
  return (
    <div className="qc-tools">
      {/* 검색 + 칩은 한 덩어리로 왼쪽에, 정렬은 오른쪽 끝(.qc-sort 의 margin-left:auto) */}
      <div className="qc-tools__group">
        <input
          className="qc-search"
          type="search"
          placeholder={t('filter.search')}
          aria-label={t('filter.search')}
          value={query}
          onChange={(e) => onQueryChange(e.target.value)}
        />
        <div className="qc-chips" role="group" aria-label={t('filter.label')}>
          {CHIP_KEYS.map((k) => (
            <button
              key={k}
              type="button"
              className={cls('qc-chip', chips[k] && 'is-on')}
              aria-pressed={chips[k]}
              onClick={() => onToggleChip(k)}
            >
              {t(`filter.${k}`)}
            </button>
          ))}
        </div>
      </div>
      {/* 선택지가 셋뿐이라 네이티브 select — 커스텀 드롭다운은 과하다. 상태는 WikiPage 소유(§3.3) */}
      <select
        className="qc-sort"
        aria-label={t('sort.label')}
        value={sort}
        onChange={(e) => onSortChange(e.target.value as SortKey)}
      >
        {SORT_KEYS.map((k) => (
          <option key={k} value={k}>{t(`sort.${k}`)}</option>
        ))}
      </select>
    </div>
  )
}
