import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import './styles.css'

const container = document.getElementById('questcodex-root')
if (!container) {
  throw new Error('QuestCodex: #questcodex-root not found')
}

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
