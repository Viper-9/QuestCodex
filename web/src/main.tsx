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
