import { StrictMode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { App } from './App'
import './styles.css'

let root: Root | null = null

export function mount(id: string) {
  const container = document.getElementById(id)
  if (!container) {
    throw new Error(`QuestCodex: #${id} not found`)
  }
  root?.unmount()
  root = createRoot(container)
  root.render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}

export function unmount() {
  root?.unmount()
  root = null
}

/**
 * Razor 호스트(Blazor 서킷)가 JS interop 으로 호출한다. React 는 window 이벤트로 받는다.
 * 데이터는 싣지 않는다 — 알림만 보내고 진행 상태는 REST 로 다시 요청한다.
 */
export function notify(msg: unknown) {
  window.dispatchEvent(new CustomEvent('questcodex:message', { detail: msg }))
}
