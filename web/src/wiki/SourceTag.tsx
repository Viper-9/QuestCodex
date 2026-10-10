import type { CatalogQuest } from '../api/catalog'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'

/**
 * 퀘스트 이름 옆의 출처 태그. 모드 퀘스트는 모드 이름, 모드가 덮어쓴 바닐라 퀘스트도 모드 이름(점선 테두리로 구분),
 * 그 밖의 바닐라 퀘스트는 아무것도 그리지 않는다. color 는 derive.assignModColors() 의 번호.
 * compact(태그 간소화)면 이름 대신 모드 칩 줄과 같은 색 점 — 덮어쓴 바닐라는 속이 빈 점. 이름은 툴팁으로.
 */
export function SourceTag({ quest, color, compact }: { quest: CatalogQuest; color?: number; compact?: boolean }) {
  const t = useT()
  if (quest.isVanilla && !quest.overriddenBy) return null
  if (compact) {
    const name = quest.isVanilla ? quest.overriddenBy! : quest.modName ?? t('tag.mod')
    const label = quest.isVanilla ? `${name} — ${t('tag.overriddenTitle')}` : name
    return (
      <span
        className={cls('qc-modstrip__dot', 'qc-srcdot', quest.isVanilla && 'qc-modstrip__dot--override')}
        data-mod-color={color} role="img" aria-label={label} title={label}
      />
    )
  }
  if (quest.isVanilla) {
    return (
      <span className="qc-tag qc-tag--mod qc-tag--override" data-mod-color={color} title={t('tag.overriddenTitle')}>
        {quest.overriddenBy}
      </span>
    )
  }
  return (
    <span className="qc-tag qc-tag--mod" data-mod-color={color} title={quest.modName ?? undefined}>
      {quest.modName ?? t('tag.mod')}
    </span>
  )
}
