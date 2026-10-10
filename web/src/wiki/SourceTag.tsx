import type { CatalogQuest } from '../api/catalog'
import { useT } from '../i18n/I18nContext'

/**
 * 퀘스트 이름 옆의 출처 태그. 모드 퀘스트는 모드 이름, 모드가 덮어쓴 바닐라 퀘스트도 모드 이름(점선 테두리로 구분),
 * 그 밖의 바닐라 퀘스트는 아무것도 그리지 않는다. color 는 derive.assignModColors() 의 번호.
 */
export function SourceTag({ quest, color }: { quest: CatalogQuest; color?: number }) {
  const t = useT()
  if (quest.isVanilla) {
    if (!quest.overriddenBy) return null
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
