import type { Catalog, CatalogQuest, Reward } from '../api/catalog'
import { useT } from '../i18n/I18nContext'
import type { UiKey } from '../i18n/index'
import type { NameLookup } from './derive'
import { formatObjective, formatRequirement, formatReward, shortId } from './format'
import { LineList } from './LineList'

interface QuestDetailProps {
  quest: CatalogQuest
  catalog: Catalog
  lookup: NameLookup
  onOpenDescription(questId: string): void
  onJump(questId: string): void
}

/** (d) 펼친 행. 2열 [목표·시작 조건] [보상] + 전폭 푸터 [연계]. 설명 본문은 여기 없음 — 팝업 (§1.2 d, e). */
export function QuestDetail({ quest, catalog, lookup, onOpenDescription, onJump }: QuestDetailProps) {
  const t = useT()
  const meta = [
    lookup.traderName(quest.traderId),
    quest.isVanilla ? t('detail.vanilla') : (quest.modName ?? t('detail.mod')),
    quest.minLevel !== null ? t('detail.minLevel', { n: quest.minLevel }) : null,
    quest.factionOnly === 'bear' ? t('detail.bearOnly') : quest.factionOnly === 'usec' ? t('detail.usecOnly') : null,
  ].filter(Boolean).join(' · ')
  const hasDescription = quest.description.trim() !== ''
  const hasRelated = quest.prerequisites.length > 0 || quest.unlocks.length > 0

  return (
    <div className="qc-detail">
      <div className="qc-detail__bar">
        <button
          type="button"
          className="qc-btn"
          disabled={!hasDescription}
          title={hasDescription ? undefined : t('detail.noDescription')}
          onClick={() => onOpenDescription(quest.id)}
        >
          {t('detail.description')}
        </button>
        <span className="qc-detail__meta">{meta}</span>
      </div>
      <div className="qc-detail__grid">
        <section>
          <h4 className="qc-detail__h">{t('detail.objectives')}</h4>
          <LineList lines={quest.objectives.map((o) => formatObjective(o, t))} empty={t('detail.noObjectives')} onJump={onJump} />
          <h4 className="qc-detail__h">{t('detail.requirements')}</h4>
          <LineList lines={quest.requirements.map((r) => formatRequirement(r, lookup, t))} empty={t('detail.noRequirements')} onJump={onJump} />
        </section>
        <section>
          <h4 className="qc-detail__h">{t('detail.rewards')}</h4>
          <LineList lines={quest.rewards.success.map((r) => formatReward(r, lookup, t))} empty={t('detail.noRewards')} onJump={onJump} />
          <ExtraRewards titleKey="detail.rewardsStarted" rewards={quest.rewards.started} lookup={lookup} onJump={onJump} />
          <ExtraRewards titleKey="detail.rewardsFail" rewards={quest.rewards.fail} lookup={lookup} onJump={onJump} />
        </section>
      </div>
      {hasRelated
        ? <RelatedQuests quest={quest} catalog={catalog} lookup={lookup} onJump={onJump} />
        : quest.tags.includes('isolated') && <p className="qc-detail__note qc-detail__foot">{t('detail.isolated')}</p>}
    </div>
  )
}

interface ExtraRewardsProps {
  titleKey: UiKey                          // detail.rewardsStarted | detail.rewardsFail ({n} = 개수)
  rewards: Reward[]
  lookup: NameLookup
  onJump(questId: string): void
}

/** started/fail 보상은 비어 있지 않을 때만, 접힌 채로 (§1.2 d) */
function ExtraRewards({ titleKey, rewards, lookup, onJump }: ExtraRewardsProps) {
  const t = useT()
  if (rewards.length === 0) return null
  return (
    <details className="qc-extra">
      <summary>{t(titleKey, { n: rewards.length })}</summary>
      <LineList lines={rewards.map((r) => formatReward(r, lookup, t))} empty="" onJump={onJump} />
    </details>
  )
}

type RelatedProps = Omit<QuestDetailProps, 'onOpenDescription'>

/** 연계는 제목 없이 구분선 아래 [선행] [후속] 두 열로만 (§1.2 d) */
function RelatedQuests({ quest, catalog, lookup, onJump }: RelatedProps) {
  return (
    <div className="qc-detail__foot">
      <div className="qc-related">
        <RelatedColumn labelKey="detail.prereq" ids={quest.prerequisites} quest={quest} catalog={catalog} lookup={lookup} onJump={onJump} />
        <RelatedColumn labelKey="detail.unlocks" ids={quest.unlocks} quest={quest} catalog={catalog} lookup={lookup} onJump={onJump} />
      </div>
    </div>
  )
}

function RelatedColumn({ labelKey, ids, quest, catalog, lookup, onJump }: RelatedProps & { labelKey: UiKey; ids: string[] }) {
  const t = useT()
  if (ids.length === 0) return null
  return (
    <div className="qc-related__col">
      <div className="qc-related__k">{t(labelKey)}</div>
      {ids.map((id) => {
        const target = catalog.quests[id]
        if (!target) return <span key={id} className="qc-warn">{t('fmt.unknownQuest', { id: shortId(id) })}</span>
        return (
          <button key={id} type="button" className="qc-link" onClick={() => onJump(id)}>
            {target.name}
            {target.traderId !== quest.traderId && <span className="qc-related__who"> {lookup.traderName(target.traderId)}</span>}
          </button>
        )
      })}
    </div>
  )
}
