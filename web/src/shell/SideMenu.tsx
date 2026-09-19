import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import type { Page } from './router'

interface SideMenuProps {
  page: Page
  onNavigate(page: Page): void
}

// 라벨 키는 `menu.${page}` — en.json/ko.json 에 menu.wiki, menu.progress
const PAGES: readonly Page[] = ['wiki', 'progress']

export function SideMenu({ page, onNavigate }: SideMenuProps) {
  const t = useT()
  return (
    <nav className="qc-side" aria-label={t('menu.label')}>
      <button type="button" className="qc-side__logo" onClick={() => onNavigate('wiki')}>QuestCodex</button>
      {PAGES.map((p) => (
        <button
          key={p}
          type="button"
          className={cls('qc-side__item', p === page && 'is-active')}
          aria-current={p === page ? 'page' : undefined}
          onClick={() => onNavigate(p)}
        >
          {t(`menu.${p}`)}
        </button>
      ))}
    </nav>
  )
}
