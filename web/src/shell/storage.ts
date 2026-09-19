/** localStorage 는 프라이빗 모드·차단 설정에서 접근 자체가 throw 할 수 있다. 실패하면 null. */
export function safeStorage(): Storage | null {
  try {
    return globalThis.localStorage ?? null
  } catch {
    return null
  }
}
