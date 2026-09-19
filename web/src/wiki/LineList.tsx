import { cls } from '../cls'
import type { FormattedLine } from './format'

interface LineListProps {
  lines: FormattedLine[]
  /** 비어 있을 때 한 줄 문구 ("목표 정보 없음" 등). 빈 문자열이면 아무것도 안 그린다. */
  empty: string
  onJump(questId: string): void
}

/** FormattedLine[] → 불릿 목록. questId 가 있는 조각은 연계 점프 버튼. */
export function LineList({ lines, empty, onJump }: LineListProps) {
  if (lines.length === 0) return empty ? <p className="qc-detail__note">{empty}</p> : null
  return (
    <ul className="qc-lines">
      {lines.map((line, i) => (
        <li key={i} className={cls(line.tone === 'muted' && 'qc-muted', line.tone === 'warn' && 'qc-warn')}>
          <span>
            {line.parts.map((p, j) => {
              const id = p.questId
              return id
                ? <button key={j} type="button" className="qc-link" onClick={() => onJump(id)}>{p.text}</button>
                : <span key={j}>{p.text}</span>
            })}
          </span>
        </li>
      ))}
    </ul>
  )
}
