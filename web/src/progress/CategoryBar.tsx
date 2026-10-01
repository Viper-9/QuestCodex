import type { ReactNode } from 'react'
import type { CatalogItemCategory } from '../api/catalog'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { formatInt } from '../wiki/format'
import { OTHER_CATEGORY } from './derive'
import { itemCategoryLabel } from './format'

interface CategoryBarProps {
  /** 카탈로그의 카테고리(표시 순서) */
  categories: CatalogItemCategory[]
  /** 행이 하나라도 있는 카테고리. OTHER_CATEGORY 가 있으면 맨 뒤에 '기타'로 붙는다 */
  present: ReadonlySet<string>
  /** 지금 칩 안에서 센 카테고리별 행 수 */
  counts: ReadonlyMap<string, number>
  /** null = 전체 */
  value: string | null
  onChange(category: string | null): void
}

/** 핸드북 카테고리 아이콘 줄(필요 아이템·해금 경로 공용). 고른 카테고리를 다시 누르면 전체로. 카테고리가 하나뿐이면 그리지 않는다. */
export function CategoryBar({ categories, present, counts, value, onChange }: CategoryBarProps) {
  const t = useT()
  const known = categories.filter((c) => present.has(c.id))
  const list = present.has(OTHER_CATEGORY) ? [...known, { id: OTHER_CATEGORY, iconUrl: null }] : known
  if (list.length <= 1) return null

  return (
    <div className="qc-cats" role="group" aria-label={t('items.cat.label')}>
      <CategoryButton label={t('items.cat.all')} on={value === null} onClick={() => onChange(null)}>
        <svg className="qc-cat__icon" viewBox="0 0 16 16" aria-hidden="true">
          <rect x="2" y="2" width="5" height="5" rx="1" /><rect x="9" y="2" width="5" height="5" rx="1" />
          <rect x="2" y="9" width="5" height="5" rx="1" /><rect x="9" y="9" width="5" height="5" rx="1" />
        </svg>
      </CategoryButton>
      {list.map((c) => {
        const label = itemCategoryLabel(c.id, t)
        return (
          <CategoryButton key={c.id} label={label} count={counts.get(c.id) ?? 0} on={value === c.id} onClick={() => onChange(value === c.id ? null : c.id)}>
            {c.iconUrl ? <img className="qc-cat__icon" src={c.iconUrl} alt="" /> : <span className="qc-cat__text">{label}</span>}
          </CategoryButton>
        )
      })}
    </div>
  )
}

function CategoryButton({ label, count, on, onClick, children }: { label: string; count?: number; on: boolean; onClick(): void; children: ReactNode }) {
  return (
    <button type="button" className={cls('qc-cat', on && 'is-on', count === 0 && 'is-empty')} aria-pressed={on} title={label} aria-label={label} onClick={onClick}>
      {children}
      {count !== undefined && <span className="qc-cat__count">{formatInt(count)}</span>}
    </button>
  )
}

/** 카테고리별 개수 */
export function countBy<T>(rows: T[], key: (row: T) => string): Map<string, number> {
  const out = new Map<string, number>()
  for (const r of rows) out.set(key(r), (out.get(key(r)) ?? 0) + 1)
  return out
}
