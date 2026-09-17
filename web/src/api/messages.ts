export type QuestCodexMessage = { type: 'profileUpdated'; profileId: string }

const EVENT = 'questcodex:message'

function isMessage(x: unknown): x is QuestCodexMessage {
  return typeof x === 'object' && x !== null && (x as { type?: unknown }).type === 'profileUpdated'
    && typeof (x as { profileId?: unknown }).profileId === 'string'
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
