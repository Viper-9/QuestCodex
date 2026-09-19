import type { ReactNode } from 'react'
import type { CatalogQuest } from '../api/catalog'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'

/** 연계 점프·딥링크가 scrollIntoView 대상으로 쓰는 DOM id (§4.4). */
export function rowId(questId: string): string {
  return `qc-quest-${questId}`
}

interface QuestRowProps {
  quest: CatalogQuest
  traderName: string
  expanded: boolean
  onToggle(): void
  /** 펼쳤을 때 행 아래에 붙는 내용 (Task 6 의 QuestDetail). 접힌 행은 undefined. */
  detail?: ReactNode
}

export function QuestRow({ quest, traderName, expanded, onToggle, detail }: QuestRowProps) {
  const t = useT()
  const prereq = quest.prerequisites.length
  return (
    <li id={rowId(quest.id)} className={cls('qc-quest', expanded && 'is-open')}>
      {/* 행 전체가 클릭 영역. <a> 가 아니라 <button> (Blazor 호스트 주의, §4.6) */}
      <button type="button" className="qc-row" aria-expanded={expanded} onClick={onToggle}>
        <span className="qc-row__name">
          {quest.name}
          {!quest.isVanilla && (
            <span className="qc-tag qc-tag--mod" title={quest.modName ?? undefined}>
              {quest.modName ?? t('tag.mod')}
            </span>
          )}
        </span>
        <span className="qc-row__trader">{traderName}</span>
        <span className="qc-row__num">{quest.minLevel ?? '—'}</span>
        <span className="qc-row__num qc-row__prereq">{prereq > 0 ? prereq : '—'}</span>
        <span className="qc-row__caret" aria-hidden="true">{expanded ? '⌄' : '›'}</span>
      </button>
      {expanded && detail}
    </li>
  )
}
