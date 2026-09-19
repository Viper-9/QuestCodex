import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import type { ChipKey, Chips } from './derive'

interface FilterBarProps {
  query: string
  onQueryChange(query: string): void
  chips: Chips
  onToggleChip(key: ChipKey): void
  count: number                            // 필터 적용 후 개수
}

// 라벨 키는 `filter.${key}` — en.json/ko.json 의 filter.vanilla / filter.mod / filter.bear / filter.usec
const CHIP_KEYS: readonly ChipKey[] = ['vanilla', 'mod', 'bear', 'usec']

export function FilterBar({ query, onQueryChange, chips, onToggleChip, count }: FilterBarProps) {
  const t = useT()
  return (
    <div className="qc-tools">
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
      {/* v1.0 은 레벨 오름차순 고정 — 라벨만 (§1.2 b). 정렬 문구도 filter.count 에 포함 */}
      <span className="qc-tools__count">{t('filter.count', { n: count })}</span>
    </div>
  )
}
