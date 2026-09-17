/** changedQuestIds 가 없으면 무엇이 바뀌었는지 모르는 경우(레이드 종료) — progress 전체를 다시 요청한다. */
export type QuestCodexMessage = { type: 'profileUpdated'; profileId: string; changedQuestIds?: string[] }

const EVENT = 'questcodex:message'

function isMessage(x: unknown): x is QuestCodexMessage {
  if (typeof x !== 'object' || x === null) return false
  const m = x as { type?: unknown; profileId?: unknown; changedQuestIds?: unknown }
  return m.type === 'profileUpdated'
    && typeof m.profileId === 'string'
    && (m.changedQuestIds === undefined || (Array.isArray(m.changedQuestIds) && m.changedQuestIds.every((id) => typeof id === 'string')))
}

/** 서버 푸시 구독. 반환값을 호출하면 해제된다. */
export function subscribeMessages(handler: (msg: QuestCodexMessage) => void): () => void {
  const listener = (e: Event) => {
    const detail = (e as CustomEvent).detail
    if (isMessage(detail)) handler(detail)
  }
  window.addEventListener(EVENT, listener)
  return () => window.removeEventListener(EVENT, listener)
}
