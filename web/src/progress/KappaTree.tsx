import { useEffect, useLayoutEffect, useMemo, useRef, useState, type MouseEvent as ReactMouseEvent, type PointerEvent as ReactPointerEvent, type RefObject } from 'react'
import type { Catalog } from '../api/catalog'
import type { ProfileProgress } from '../api/progress'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { navigate } from '../shell/router'
import type { NameLookup } from '../wiki/derive'
import { questProgress } from './derive'
import {
  edgePath, kappaTree, TREE_CX, TREE_H, TREE_PAD, TREE_RY, TREE_W, treeFocus, treeX, treeY,
  type ChainStat, type KappaGraph, type TreeNode,
} from './kappa'
import { useKappaDialogs } from './KappaDialogs'
import { KappaTreeDetail } from './KappaTreeDetail'

interface KappaTreeProps {
  catalog: Catalog
  graph: KappaGraph
  progress: ProfileProgress
  lookup: NameLookup
  done: ReadonlySet<string>
  stats: ReadonlyMap<string, ChainStat>
  traderId: string
  collapse: boolean
  onCollapse(v: boolean): void
}

/** 간선을 그리는 순서 — 지금 할 것에서 나가는 간선이 맨 위 */
const EDGE_RANK = { done: 0, far: 1, next: 1, now: 2 } as const

/** 트리 영역 최소 높이(px) — 한두 줄짜리 트리도 답답하지 않게. 트리가 더 낮으면 처음엔 세로 가운데에 둔다 */
const MIN_VIEW_H = 260

/** 이만큼(px) 넘게 움직여야 끌기로 본다 — 그보다 작으면 노드 클릭 */
const DRAG_SLOP = 4

/**
 * 마우스로 잡고 끌어 이동. 가로·세로 모두 트리 스크롤만 움직이고 페이지는 그대로 둔다.
 * 터치는 브라우저 기본 스크롤에 맡긴다. 끌었으면 손을 뗄 때의 클릭(노드 선택)을 삼킨다.
 */
function useDragPan(scrollRef: RefObject<HTMLDivElement | null>) {
  const dragged = useRef(false)
  const [dragging, setDragging] = useState(false)

  const onPointerDown = (e: ReactPointerEvent) => {
    const el = scrollRef.current
    if (!el || e.pointerType !== 'mouse' || e.button !== 0) return
    const start = { x: e.clientX, y: e.clientY, left: el.scrollLeft, top: el.scrollTop }
    dragged.current = false
    const move = (ev: PointerEvent) => {
      const dx = ev.clientX - start.x
      const dy = ev.clientY - start.y
      if (!dragged.current) {
        if (Math.abs(dx) < DRAG_SLOP && Math.abs(dy) < DRAG_SLOP) return
        dragged.current = true
        setDragging(true)
      }
      el.scrollLeft = start.left - dx
      el.scrollTop = start.top - dy
    }
    const up = () => {
      window.removeEventListener('pointermove', move)
      window.removeEventListener('pointerup', up)
      setDragging(false)
    }
    window.addEventListener('pointermove', move)
    window.addEventListener('pointerup', up)
  }
  const onClickCapture = (e: ReactMouseEvent) => {
    if (!dragged.current) return
    dragged.current = false
    e.stopPropagation()
    e.preventDefault()
  }
  return { dragging, onPointerDown, onClickCapture }
}

/**
 * 상인 하나의 카파 퀘스트 트리(kappa-tree.spec.md §2). 노드 = 절대 위치 버튼, 뒤에 SVG 곡선 간선.
 * 노드를 고르면 조상·자손만 진하게 하고 카드 아래에 상세 카드를 연다.
 */
export function KappaTree({ catalog, graph, progress, lookup, done, stats, traderId, collapse, onCollapse }: KappaTreeProps) {
  const t = useT()
  const tree = useMemo(() => kappaTree(catalog, graph, progress, done, traderId, collapse), [catalog, graph, progress, done, traderId, collapse])
  // 선택은 상인과 묶어 둔다 — 상인 탭을 바꾸면 effect 없이 해제된다(연쇄 팝업의 { key, … } 와 같은 수법)
  const [picked, setPicked] = useState<{ trader: string; id: string } | null>(null)
  const selected = picked && picked.trader === traderId && tree.nodes.some((n) => n.id === picked.id) ? picked.id : null
  const focus = useMemo(() => (selected ? treeFocus(tree, selected) : null), [tree, selected])
  const scrollRef = useRef<HTMLDivElement>(null)
  const pan = useDragPan(scrollRef)
  /** 가로 스크롤바 높이 — 보이는 영역을 트리 높이만큼 확보하려고 잰다 */
  const [bar, setBar] = useState(0)
  const [jumpTarget, setJumpTarget] = useState<string | null>(null)
  const { open: openDialog, dialogs } = useKappaDialogs(catalog, graph, progress, done, lookup)

  const width = TREE_PAD * 2 + Math.max(0, tree.cols - 1) * TREE_CX + TREE_W
  const height = TREE_PAD * 2 + Math.max(0, tree.rows - 1) * TREE_RY + TREE_H
  // 보이는 영역은 트리 높이(최소 MIN_VIEW_H), 위아래에 그 절반씩 빈 공간을 숨겨 둔다 — 끌어서 맨 위·아래 노드도 가운데까지 올 수 있게
  const viewH = Math.max(height, MIN_VIEW_H)
  const slack = Math.round(viewH / 2)

  useLayoutEffect(() => {
    const el = scrollRef.current
    if (el) setBar(el.offsetHeight - el.clientHeight)
  }, [width, height])

  // 상인 변경·트리 모드 진입·접기 토글 때만: 가장 왼쪽 "지금 할 수 있음"(없으면 "다음에 열림") 노드가 보이게(§2.4)
  useLayoutEffect(() => {
    const el = scrollRef.current
    if (!el) return
    const xs = (['now', 'next'] as const)
      .map((s) => tree.nodes.filter((n) => n.kind === 'quest' && n.state === s).map((n) => treeX(n.col)))
      .find((list) => list.length > 0) ?? [0]
    el.scrollLeft = Math.max(0, Math.min(...xs) - 0.6 * TREE_CX)
    el.scrollTop = slack - Math.round((viewH - height) / 2)
  }, [traderId, collapse]) // tree 는 일부러 뺀다 — 선택·진행 갱신 때는 스크롤을 그대로 둔다

  useEffect(() => {
    if (!jumpTarget) return
    scrollRef.current?.querySelector(`[data-node="${jumpTarget}"]`)?.scrollIntoView({ block: 'nearest', inline: 'center', behavior: 'smooth' })
    setJumpTarget(null)
  }, [jumpTarget])

  /** 접기를 켜서 고른 노드가 사라지면 선택도 해제(다시 펼쳐도 되살아나지 않게) */
  const toggleCollapse = (v: boolean) => {
    if (v && selected && tree.nodes.find((n) => n.id === selected)?.state === 'done') setPicked(null)
    onCollapse(v)
  }
  const pick = (n: TreeNode) => {
    if (n.kind === 'doneBox') { onCollapse(false); return }
    setPicked(selected === n.id ? null : { trader: traderId, id: n.id })
  }
  /** 상세 카드의 연계 링크: 트리에 그려져 있으면 그 노드를 고르고 스크롤, 아니면 위키로 */
  const jumpTo = (id: string) => {
    if (!tree.nodes.some((n) => n.id === id && n.kind !== 'doneBox')) { navigate('wiki', null, { quest: id }); return }
    setPicked({ trader: traderId, id })
    setJumpTarget(id)
  }

  const sub = (n: TreeNode) => {
    if (n.kind === 'doneBox') return t('kappa.tree.doneBoxHint')
    const qp = questProgress(progress, n.id)
    if (n.kind === 'outside') {
      const trader = lookup.traderName(catalog.quests[n.id]?.traderId ?? '')
      return n.state === 'done' ? `${trader} · ${t('status.Success')}` : trader
    }
    if (n.state === 'done') return qp.status === 'Success' ? null : <span className="qc-warn">{t('kappa.tree.failOk')}</span>
    if (n.state === 'now') return t(`status.${qp.status}` as 'status.Started')
    if (n.state === 'next') {
      const level = qp.lockReasons.find((r) => r.kind === 'level')
      return level ? t('kappa.tree.level', { n: level.need }) : t('kappa.tree.next')
    }
    return null
  }

  const node = (n: TreeNode) => {
    const name = n.kind === 'doneBox' ? t('kappa.tree.doneBox', { n: tree.doneCount }) : catalog.quests[n.id]?.name ?? n.id
    const label = n.kind === 'quest' && n.state === 'done' ? `✓ ${name}` : name
    const s = sub(n)
    return (
      <button
        key={n.id} type="button" data-node={n.id} title={n.kind === 'doneBox' ? undefined : name} onClick={() => pick(n)}
        aria-pressed={n.kind === 'doneBox' ? undefined : n.id === selected}
        className={cls(
          'qc-ktree__node',
          n.kind === 'doneBox' ? 'qc-ktree__node--box' : n.kind === 'outside' ? 'qc-ktree__node--out' : `qc-ktree__node--${n.state}`,
          n.kind === 'outside' && n.state === 'done' && 'qc-ktree__node--done',
          n.id === selected ? 'is-sel' : focus && (focus.has(n.id) ? 'is-hi' : 'is-dim'),
        )}
        style={{ left: treeX(n.col), top: treeY(n.row), width: TREE_W, height: TREE_H }}
      >
        <span className="qc-ktree__name">{label}</span>
        {s && <span className="qc-ktree__sub">{s}</span>}
      </button>
    )
  }

  const state = new Map(tree.nodes.map((n) => [n.id, n.state]))
  const traderName = lookup.traderName(traderId)
  const selectedNode = selected ? catalog.quests[selected] : undefined

  return <>
    <section className="qc-card qc-card--flush qc-ktree" aria-label={t('kappa.tree.aria', { trader: traderName })}>
      <div className="qc-ktree__head">
        <span className="qc-ktree__title">{t('kappa.tree.title', { trader: traderName })}</span>
        <span>{t('kappa.tree.count', { left: tree.total - tree.doneCount, done: tree.doneCount, total: tree.total })}</span>
        <span className="qc-ktree__sp" />
        <label className="qc-ktree__toggle">
          <input type="checkbox" checked={collapse} onChange={(e) => toggleCollapse(e.target.checked)} /> {t('kappa.tree.collapse')}
        </label>
      </div>
      <div
        className={cls('qc-ktree__scroll', pan.dragging && 'is-dragging')} ref={scrollRef}
        style={{ height: viewH + bar }}
        onPointerDown={pan.onPointerDown} onClickCapture={pan.onClickCapture} onDragStart={(e) => e.preventDefault()}
      >
        <div className="qc-ktree__canvas" style={{ width, height, margin: `${slack}px 0` }}>
          <svg width={width} height={height} aria-hidden="true">
            {[...tree.edges].sort((a, b) => EDGE_RANK[state.get(a.from)!] - EDGE_RANK[state.get(b.from)!]).map((e) => (
              <path
                key={`${e.from}>${e.to}`} d={edgePath(e, tree)}
                className={cls(
                  state.get(e.from) === 'now' && 'is-now', state.get(e.from) === 'done' && 'is-done', e.soft && 'is-soft',
                  focus && (focus.has(e.from) && focus.has(e.to) ? 'is-hi' : 'is-dim'),
                )}
              />
            ))}
          </svg>
          {tree.nodes.map(node)}
        </div>
      </div>
      <div className="qc-ktree__legend">
        {(['done', 'now', 'next', 'far'] as const).map((s) => (
          <span key={s}><i className={`qc-ktree__sw qc-ktree__sw--${s}`} />{t(`kappa.tree.legend.${s}`)}</span>
        ))}
        <span><i className="qc-ktree__ln" />{t('kappa.tree.legend.soft')}</span>
      </div>
    </section>
    {selectedNode && (
      <KappaTreeDetail
        quest={selectedNode} catalog={catalog} progress={progress} lookup={lookup} done={done} stats={stats}
        openDialog={openDialog} onJump={jumpTo} onClose={() => setPicked(null)}
      />
    )}
    {dialogs}
  </>
}

/** 트리 모드 + 전체 탭 — 트리 카드 자리의 안내 한 줄 */
export function KappaTreePick() {
  const t = useT()
  return <section className="qc-card qc-ktree__pick"><p className="qc-muted">{t('kappa.tree.pickTrader')}</p></section>
}
