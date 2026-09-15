import { useEffect, useState } from 'react'
import { fetchPing, type PingResponse } from './api/ping'

type State =
  | { kind: 'loading' }
  | { kind: 'ok'; data: PingResponse }
  | { kind: 'error'; message: string }

export function App() {
  const [state, setState] = useState<State>({ kind: 'loading' })

  useEffect(() => {
    let cancelled = false
    fetchPing()
      .then((data) => { if (!cancelled) setState({ kind: 'ok', data }) })
      .catch((err: unknown) => {
        if (!cancelled) setState({ kind: 'error', message: err instanceof Error ? err.message : String(err) })
      })
    return () => { cancelled = true }
  }, [])

  return (
    <main className="qc-shell">
      <h1>QuestCodex</h1>
      {state.kind === 'loading' && <p>Connecting to SPT server…</p>}
      {state.kind === 'error' && <p className="qc-error">Error: {state.message}</p>}
      {state.kind === 'ok' && (
        <dl className="qc-ping">
          <dt>mod</dt><dd>{state.data.mod}</dd>
          <dt>version</dt><dd>{state.data.version}</dd>
          <dt>spt</dt><dd>{state.data.spt}</dd>
        </dl>
      )}
    </main>
  )
}
