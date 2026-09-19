/** className 조합. falsy 는 버린다: cls('qc-chip', on && 'is-on') */
export function cls(...parts: (string | false | null | undefined)[]): string {
  return parts.filter(Boolean).join(' ')
}
