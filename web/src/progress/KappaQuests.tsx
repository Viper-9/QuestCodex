import { useEffect, useMemo, useState, type ReactNode } from 'react'
import type { Catalog, CatalogQuest } from '../api/catalog'
import type { ProfileProgress } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { navigate } from '../shell/router'
import { branchIndex, toggleMember, type NameLookup } from '../wiki/derive'
import { formatInt } from '../wiki/format'
import { QuestDetail } from '../wiki/QuestDetail'
import { questProgress } from './derive'
import { requirementLines } from './format'
import type { ChainStat, KappaGraph, KappaLists } from './kappa'
import { useKappaDialogs } from './KappaDialogs'
import { lineId, QuestLine } from './OverviewView'
import { objectiveLines } from './parts'

type Section = 'now' | 'locked' | 'done'

interface KappaQuestsProps {
  catalog: Catalog
  graph: KappaGraph
  progress: ProfileProgress
  lookup: NameLookup
  lists: KappaLists
  done: ReadonlySet<string>
  stats: ReadonlyMap<string, ChainStat>
  /** 퀘스트 → 그것에 막힌 잠긴 카파 퀘스트(줄 오른쪽 "→ ○○ 외 N개") */
  unlocks: ReadonlyMap<string, CatalogQuest[]>
  /** 아직 아무 퀘스트도 열리지 않은 프로필(게임 미접속) */
  fresh: boolean
}

/** 새 프로필 안내에 보여 줄 "가장 먼저 열릴 것" 수 */
const FIRST_N = 4

/** 카파 퀘스트 목록 — 지금 할 수 있는 것(펼침) / 잠김·완료(접힘), 구역마다 카드 하나. 줄과 펼친 상세는 현황 탭 것을 그대로 쓴다. */
export function KappaQuests({ catalog, graph, progress, lookup, lists, done, stats, unlocks, fresh }: KappaQuestsProps) {
  const t = useT()
  const [openSections, setOpenSections] = useState<ReadonlySet<Section>>(() => new Set(['now']))
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  const { open: openDialog, dialogs } = useKappaDialogs(catalog, graph, progress, done, lookup)
  const [scrollTarget, setScrollTarget] = useState<string | null>(null)
  const branches = useMemo(() => branchIndex(Object.values(catalog.quests)), [catalog])

  /** 연계 링크: 카파 퀘스트면 그 구역을 열고 펼쳐 스크롤, 아니면(또는 상인 필터로 안 보이면) 위키로 */
  const jumpTo = (id: string) => {
    const section = (['now', 'locked', 'done'] as const).find((s) => lists[s].some((q) => q.id === id))
    if (!section) { navigate('wiki', null, { quest: id }); return }
    setOpenSections((prev) => new Set(prev).add(section))
    setExpanded((prev) => new Set(prev).add(id))
    setScrollTarget(id)
  }

  useEffect(() => {
    if (!scrollTarget) return
    document.getElementById(lineId(scrollTarget))?.scrollIntoView({ block: 'start', behavior: 'smooth' })
    setScrollTarget(null)
  }, [scrollTarget])

  const line = (q: CatalogQuest) => {
    const qp = questProgress(progress, q.id)
    const open = expanded.has(q.id)
    const s = stats.get(q.id)
    const opens = qp.status === 'Locked' ? [] : unlocks.get(q.id) ?? []
    const numbers = [
      s && s.after > 0 ? t('kappa.after', { n: formatInt(s.after) }) : null,
      s && s.chain > 0 ? t('kappa.chain', { n: s.chain }) : null,
    ].filter(Boolean).join(' · ')
    // 열 수 있는 퀘스트 줄은 "끝내면 열림"을 이름으로 보여 주고 숫자는 툴팁으로, 잠긴 줄은 숫자 그대로
    const asideText = opens.length === 0 ? numbers
      : opens.length === 1 ? t('kappa.opens', { name: opens[0].name })
      : t('kappa.opensMore', { name: opens[0].name, n: opens.length - 1 })
    const tip = opens.length === 0 ? undefined
      : `${t('kappa.unlocks')}: ${opens.map((u) => u.name).join(', ')}${numbers ? `\n${numbers}` : ''}`
    return (
      <QuestLine
        key={q.id} quest={q} qp={qp} lookup={lookup} open={open} flash={false}
        onToggle={() => setExpanded((prev) => toggleMember(prev, q.id))}
        tag={qp.status === 'FailRestartable' && <span className="qc-tag qc-warn">{t('kappa.failedRestart')}</span>}
        aside={<span className="qc-kappa__aside" title={tip}>{asideText}</span>}
        detail={open && (
          <QuestDetail
            quest={q} catalog={catalog} lookup={lookup} branch={branches.get(q.id)}
            onOpenDescription={openDialog.description} onOpenPrep={openDialog.prep} onOpenMap={openDialog.map} onJump={jumpTo}
            objectiveLines={objectiveLines(q, qp, t)}
            requirementLines={requirementLines(q, qp, lookup, t)}
            actions={<>
              <button type="button" className="qc-btn" onClick={() => openDialog.chain(q.id)}>{t('kappa.openChain')}</button>
              <button type="button" className="qc-btn" onClick={() => navigate('wiki', null, { quest: q.id })}>{t('overview.openWiki')}</button>
            </>}
          />
        )}
      />
    )
  }

  /** 지금 할 수 있는 것이 비었는데 프로필이 아직 아무것도 안 열렸을 때 — "없음" 대신 이유와 가장 먼저 열릴 것 */
  const freshNote = (
    <div className="qc-kappa__fresh">
      <p className="qc-kappa__freshh">{t('kappa.fresh.title')}</p>
      <p className="qc-muted">{t('kappa.fresh.body')}</p>
      {lists.locked.length > 0 && (
        <div className="qc-kappa__unlocks">
          <span className="qc-kappa__unlockh">{t('kappa.fresh.next')}</span>
          {lists.locked.slice(0, FIRST_N).map((q) => (
            <button key={q.id} type="button" className="qc-kappa__unlock" onClick={() => jumpTo(q.id)}>{q.name}</button>
          ))}
        </div>
      )}
    </div>
  )

  const section = (key: Section, title: string, hint: string | null, quests: CatalogQuest[]): ReactNode => {
    const open = openSections.has(key)
    let body: ReactNode
    if (quests.length === 0) body = key === 'now' && fresh ? freshNote : <p className="qc-empty">{t('kappa.empty')}</p>
    else body = <ul>{quests.map(line)}</ul>
    return (
      <section className={cls('qc-card qc-card--flush qc-kappa__sec', `qc-kappa__sec--${key}`, open && 'is-open')}>
        <button type="button" className="qc-kappa__sechead" aria-expanded={open} onClick={() => setOpenSections((prev) => toggleMember(prev, key))}>
          <span className="qc-row__caret">{open ? '▾' : '▸'}</span>
          <span className="qc-kappa__sectitle">{title}</span>
          <span className="qc-group__n">{formatInt(quests.length)}</span>
          {hint && <span className="qc-kappa__sechint">· {hint}</span>}
        </button>
        {open && body}
      </section>
    )
  }

  return (
    <div className="qc-kappa__quests">
      {section('now', t('kappa.now'), t('kappa.nowHint'), lists.now)}
      {section('locked', t('kappa.locked'), t('kappa.lockedHint'), lists.locked)}
      {section('done', t('kappa.done'), null, lists.done)}
      {dialogs}
    </div>
  )
}
