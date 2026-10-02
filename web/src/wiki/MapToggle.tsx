interface MapToggleProps {
  label: string
  /** 비활성일 때 마우스 오버 문구(이 맵에 데이터 없음) */
  noneLabel: string
  on: boolean
  /** 이 탭에 그릴 것의 수. 0 이면 비활성 */
  count: number
  onToggle(on: boolean): void
}

/** 지도 머리의 켜기/끄기 버튼(잠긴 문·탈출구). 그릴 것이 없으면 비활성이고 꺼진 모양으로 보인다. */
export function MapToggle({ label, noneLabel, on, count, onToggle }: MapToggleProps) {
  return (
    <button
      type="button"
      className={on && count > 0 ? 'qc-map__toggle is-on' : 'qc-map__toggle'}
      aria-pressed={on}
      disabled={count === 0}
      title={count === 0 ? noneLabel : undefined}
      onClick={() => onToggle(!on)}
    >
      {label}
    </button>
  )
}
