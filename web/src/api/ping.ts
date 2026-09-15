export interface PingResponse {
  mod: string
  version: string
  spt: string
}

export async function fetchPing(): Promise<PingResponse> {
  const res = await fetch('/questcodex/api/ping')
  if (!res.ok) {
    throw new Error(`ping failed: HTTP ${res.status}`)
  }
  return (await res.json()) as PingResponse
}
