import type { ReactNode } from 'react'
import type { CatalogQuest } from '../api/catalog'
import { useT } from '../i18n/I18nContext'
import type { NameLookup } from './derive'
import { QuestRow } from './QuestRow'

interface QuestListProps {
  quests: CatalogQuest[]                   // 이미 필터·정렬된 목록
  lookup: NameLookup
  expanded: ReadonlySet<string>
  onToggle(id: string): void
  renderDetail(quest: CatalogQuest): ReactNode
  /** 모드 이름 → 태그 색 번호. 카탈로그 전체로 계산된 값이라 필터를 바꿔도 색이 흔들리지 않는다. */
  modColors: Record<string, number>
}

/** (c) 컬럼 헤더 + 행 목록. 612개를 그냥 렌더한다 — 가상 스크롤 없음 (§1.2). */
export function QuestList({ quests, lookup, expanded, onToggle, renderDetail, modColors }: QuestListProps) {
  const t = useT()
  if (quests.length === 0) return <p className="qc-empty">{t('list.empty')}</p>
  return (
    <div className="qc-list">
      <div className="qc-list__header" aria-hidden="true">
        <span>{t('list.name')}</span>
        <span>{t('list.trader')}</span>
        <span className="qc-row__num">{t('list.level')}</span>
        <span className="qc-row__num">{t('list.prereq')}</span>
        <span />
      </div>
      <ul className="qc-list__body">
        {quests.map((q) => {
          const open = expanded.has(q.id)
          return (
            <QuestRow
              key={q.id}
              quest={q}
              traderName={lookup.traderName(q.traderId)}
              expanded={open}
              onToggle={() => onToggle(q.id)}
              detail={open ? renderDetail(q) : undefined}
              modColor={q.modName ? modColors[q.modName] : undefined}
            />
          )
        })}
      </ul>
    </div>
  )
}
