import { useMemo } from 'react'
import type { Catalog, CatalogQuest } from '../api/catalog'
import type { ProfileProgress } from '../api/progress'
import { useT } from '../i18n/I18nContext'
import { navigate } from '../shell/router'
import { branchIndex, type NameLookup } from '../wiki/derive'
import { formatInt } from '../wiki/format'
import { QuestDetail } from '../wiki/QuestDetail'
import { questProgress } from './derive'
import { requirementLines } from './format'
import type { ChainStat } from './kappa'
import type { KappaDialogOpeners } from './KappaDialogs'
import { objectiveLines } from './parts'

interface KappaTreeDetailProps {
  quest: CatalogQuest
  catalog: Catalog
  progress: ProfileProgress
  lookup: NameLookup
  done: ReadonlySet<string>
  stats: ReadonlyMap<string, ChainStat>
  openDialog: KappaDialogOpeners
  onJump(questId: string): void
  onClose(): void
}

/** 트리에서 고른 노드의 상세(§2.3) — 목록에서 줄을 펼친 것과 같은 QuestDetail + 머리 줄(이름·상인·상태·뒤로 N·연쇄 N) + ✕ */
export function KappaTreeDetail({ quest, catalog, progress, lookup, done, stats, openDialog, onJump, onClose }: KappaTreeDetailProps) {
  const t = useT()
  const branches = useMemo(() => branchIndex(Object.values(catalog.quests)), [catalog])
  const qp = questProgress(progress, quest.id)
  const s = stats.get(quest.id)
  const meta = [
    lookup.traderName(quest.traderId),
    done.has(quest.id) && qp.status !== 'Success' ? t('kappa.tree.failOk') : t(`status.${qp.status}` as 'status.Locked'),
    s && s.after > 0 ? t('kappa.after', { n: formatInt(s.after) }) : null,
    s && s.chain > 0 ? t('kappa.chain', { n: s.chain }) : null,
  ].filter(Boolean).join(' · ')

  return (
    <section className="qc-card qc-ktree__detail">
      <div className="qc-ktree__dhead">
        <span className="qc-ktree__dname">{quest.name}</span>
        <span className="qc-ktree__dmeta">{meta}</span>
        <button type="button" className="qc-ktree__dclose" aria-label={t('dialog.close')} onClick={onClose}>✕</button>
      </div>
      <QuestDetail
        quest={quest} catalog={catalog} lookup={lookup} branch={branches.get(quest.id)}
        onOpenDescription={openDialog.description} onOpenPrep={openDialog.prep} onOpenMap={openDialog.map} onJump={onJump}
        objectiveLines={objectiveLines(quest, qp, t)}
        requirementLines={requirementLines(quest, qp, lookup, t)}
        actions={<>
          <button type="button" className="qc-btn" onClick={() => openDialog.chain(quest.id)}>{t('kappa.openChain')}</button>
          <button type="button" className="qc-btn" onClick={() => navigate('wiki', null, { quest: quest.id })}>{t('overview.openWiki')}</button>
        </>}
      />
    </section>
  )
}
