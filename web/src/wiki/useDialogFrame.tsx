import { useEffect, useRef, type MouseEvent as ReactMouseEvent, type PointerEvent as ReactPointerEvent, type RefObject } from 'react'

type Edge = 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw'
const EDGES: Edge[] = ['n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw']
const MIN_W = 320
const MIN_H = 160
/** 창 가장자리와 띄울 최소 간격 */
const GAP = 8

const clamp = (v: number, lo: number, hi: number) => Math.min(Math.max(v, lo), hi)

interface Frame { left: number; top: number; width: number; height: number }

const storageKey = (name: string) => `qc.dialogFrame.${name}`

/** 저장해 둔 틀. localStorage 가 막혔거나(시크릿 창 등) 값이 깨졌으면 null → 기본 크기·가운데. */
function loadFrame(name: string): Frame | null {
  try {
    const f = JSON.parse(localStorage.getItem(storageKey(name)) ?? 'null') as Frame | null
    return f && [f.left, f.top, f.width, f.height].every(Number.isFinite) ? f : null
  } catch { return null }
}

function saveFrame(name: string, f: Frame | null) {
  try {
    if (f) localStorage.setItem(storageKey(name), JSON.stringify(f))
    else localStorage.removeItem(storageKey(name))
  } catch { /* 저장 못 해도 이번 세션 동작엔 지장 없음 */ }
}

/** 지금 창 안으로 끼워 넣는다 — 큰 모니터에서 저장한 틀을 작은 창에서 열어도 손잡이가 화면 밖에 있지 않게. */
function fitToViewport(f: Frame): Frame {
  const vw = window.innerWidth
  const vh = window.innerHeight
  const width = clamp(f.width, Math.min(MIN_W, vw), Math.max(vw - GAP * 2, MIN_W))
  const height = clamp(f.height, Math.min(MIN_H, vh), Math.max(vh - GAP * 2, MIN_H))
  return { width, height, left: clamp(f.left, 0, Math.max(vw - width, 0)), top: clamp(f.top, 0, Math.max(vh - height, 0)) }
}

function place(el: HTMLElement, { left, top, width, height }: Frame) {
  Object.assign(el.style, {
    margin: '0', inset: 'auto', maxWidth: 'none', maxHeight: 'none',
    left: `${left}px`, top: `${top}px`, width: `${width}px`, height: `${height}px`,
  })
}

/**
 * 팝업(<dialog>)을 창처럼 — 변·모서리 드래그로 크기 조절, 머리 드래그로 이동, 머리 더블클릭으로 기본 크기 복귀.
 * 첫 드래그 때 현재 위치·크기를 인라인 px 로 고정한다: margin:auto 가운데 정렬인 채로 폭을 바꾸면
 * 양쪽으로 늘어나 손잡이가 마우스를 절반 속도로 따라가기 때문.
 * 드래그가 끝날 때마다 틀을 localStorage 에 `name` 별로 저장하고, 다음에 열 때 그대로 복원한다.
 */
export function useDialogFrame(ref: RefObject<HTMLDialogElement | null>, name: string, open: boolean, onClose: () => void) {
  // 백드롭 클릭 판정은 누른 곳 기준 — 팝업 안에서 드래그하다 바깥에서 놓아도 닫히지 않게
  const downOnBackdrop = useRef(false)

  // 열 때 저장된 틀 복원. 이 effect 가 컴포넌트의 showModal() effect 보다 먼저 돌아 열리는 순간부터 제자리다.
  useEffect(() => {
    const el = ref.current
    if (!el || !open) return
    const saved = loadFrame(name)
    if (saved) place(el, fitToViewport(saved))
    else el.removeAttribute('style')
  }, [open, name, ref])

  function reset() {
    ref.current?.removeAttribute('style')
    saveFrame(name, null)
  }

  function start(e: ReactPointerEvent<HTMLElement>, mode: Edge | 'move') {
    const el = ref.current
    if (!el || e.button !== 0) return
    e.preventDefault()
    const r = el.getBoundingClientRect()
    let last: Frame = { left: r.left, top: r.top, width: r.width, height: r.height }
    place(el, last)

    const grip = e.currentTarget
    grip.setPointerCapture(e.pointerId)
    el.classList.add('is-dragging')
    const x0 = e.clientX
    const y0 = e.clientY
    // 그냥 클릭(더블클릭 포함)은 저장하지 않는다 — 기본 크기로 연 팝업이 클릭 한 번에 "사용자 크기"로 굳지 않게
    let moved = false

    const onMove = (ev: PointerEvent) => {
      const dx = ev.clientX - x0
      const dy = ev.clientY - y0
      const vw = window.innerWidth
      const vh = window.innerHeight
      if (dx !== 0 || dy !== 0) moved = true
      if (mode === 'move') {
        last = { ...last, left: clamp(r.left + dx, 0, vw - r.width), top: clamp(r.top + dy, 0, vh - r.height) }
      } else {
        let { left, top, right, bottom } = r
        if (mode.includes('w')) left = clamp(r.left + dx, GAP, r.right - MIN_W)
        if (mode.includes('e')) right = clamp(r.right + dx, r.left + MIN_W, vw - GAP)
        if (mode.includes('n')) top = clamp(r.top + dy, GAP, r.bottom - MIN_H)
        if (mode.includes('s')) bottom = clamp(r.bottom + dy, r.top + MIN_H, vh - GAP)
        last = { left, top, width: right - left, height: bottom - top }
      }
      place(el, last)
    }
    const onUp = () => {
      if (moved) saveFrame(name, last)
      el.classList.remove('is-dragging')
      grip.removeEventListener('pointermove', onMove)
      grip.removeEventListener('pointerup', onUp)
      grip.removeEventListener('pointercancel', onUp)
    }
    grip.addEventListener('pointermove', onMove)
    grip.addEventListener('pointerup', onUp)
    grip.addEventListener('pointercancel', onUp)
  }

  return {
    /** <dialog> 에 펼친다. padding 0 이라 target 이 dialog 자신이면 백드롭이다. */
    dialogProps: {
      onPointerDown: (e: ReactPointerEvent<HTMLDialogElement>) => { downOnBackdrop.current = e.target === e.currentTarget },
      onClick: (e: ReactMouseEvent<HTMLDialogElement>) => {
        if (downOnBackdrop.current && e.target === e.currentTarget) onClose()
        downOnBackdrop.current = false
      },
    },
    /** 머리(<header>)에 펼친다 — 버튼(✕) 위에서는 이동하지 않는다. 더블클릭은 기본 크기·가운데로 되돌리고 저장값도 지운다. */
    headProps: {
      onPointerDown: (e: ReactPointerEvent<HTMLElement>) => {
        if (!(e.target as HTMLElement).closest('button')) start(e, 'move')
      },
      onDoubleClick: (e: ReactMouseEvent<HTMLElement>) => {
        if (!(e.target as HTMLElement).closest('button')) reset()
      },
    },
    grips: EDGES.map((d) => (
      <div key={d} className={`qc-dialog__grip qc-dialog__grip--${d}`} aria-hidden onPointerDown={(e) => start(e, d)} />
    )),
  }
}
