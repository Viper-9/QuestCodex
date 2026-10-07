import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { DevDataSwitch } from './DevDataSwitch'
import { PROGRESS_SUBS, type Page, type ProgressSub, type Route } from './router'

interface SideMenuProps {
  route: Route
  onNavigate(page: Page, sub?: ProgressSub): void
}

// 라벨 키는 `menu.${page}` — en.json/kr.json 에 menu.wiki, menu.progress
const PAGES: readonly Page[] = ['wiki', 'progress']

/** 대메뉴 2개. 진행현황 아래에 소메뉴를 트리로 늘 펼쳐 둔다 (07 정보 구조: 메뉴 하나 = 질문 하나). */
export function SideMenu({ route, onNavigate }: SideMenuProps) {
  const t = useT()
  return (
    <nav className="qc-side" aria-label={t('menu.label')}>
      <button type="button" className="qc-side__logo" onClick={() => onNavigate('wiki')}>QuestCodex</button>
      {PAGES.map((p) => {
        // 진행현황은 소메뉴가 현재 위치를 표시하므로 대메뉴는 강조하지 않는다 — 둘 다 칠하면 어디 있는지 흐려진다
        const active = p === route.page && p !== 'progress'
        return (
          <div key={p}>
            <button
              type="button"
              className={cls('qc-side__item', active && 'is-active', p === route.page && 'is-open')}
              aria-current={active ? 'page' : undefined}
              onClick={() => onNavigate(p)}
            >
              {t(`menu.${p}`)}
              {p === 'progress' && (
                <svg className="qc-side__chevron" width="14" height="14" viewBox="0 0 24 24" fill="none"
                  stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <path d="M6 9l6 6 6-6" />
                </svg>
              )}
            </button>
            {p === 'progress' && (
              <div className="qc-side__sub">
                {PROGRESS_SUBS.map((s) => {
                  const on = route.page === 'progress' && route.sub === s
                  return (
                    <button
                      key={s}
                      type="button"
                      className={cls('qc-side__item', 'qc-side__item--sub', on && 'is-active')}
                      aria-current={on ? 'page' : undefined}
                      onClick={() => onNavigate('progress', s)}
                    >
                      {t(`menu.progress.${s}`)}
                    </button>
                  )
                })}
              </div>
            )}
          </div>
        )
      })}
      {/* 개발 서버에서만 — 배포 빌드에서는 import.meta.env.DEV 가 false 로 바뀌어 컴포넌트째 빠진다 */}
      {import.meta.env.DEV && <DevDataSwitch />}
    </nav>
  )
}
