import { useEffect, useState } from 'react'

/**
 * 개발 서버 전용 데이터 전환(dev-docs/ops/dev-fixtures.runbook.md). 실서버 = SPT 서버로 프록시, 그 밖 = web/fixtures/<세트>/
 * 녹화 데이터(vite.config.ts fixtures 플러그인이 쿠키 qc-fixture 를 보고 응답). SideMenu 가 import.meta.env.DEV 일 때만
 * 그리므로 배포 빌드에서는 이 모듈째 빠진다. 스타일도 배포 CSS 에 남지 않게 인라인으로 둔다. 개발 도구라 문구는 번역하지 않는다.
 */
export function DevDataSwitch() {
  const current = /(?:^|;\s*)qc-fixture=([\w-]+)/.exec(document.cookie)?.[1] ?? ''
  const [sets, setSets] = useState<string[]>([])

  useEffect(() => {
    fetch('/__qc-fixtures').then((r) => r.json() as Promise<string[]>).then(setSets).catch(() => setSets([]))
  }, [])

  // 지운 세트를 고른 채라도 실서버로 돌아올 수 있게 목록에 남긴다
  const options = current && !sets.includes(current) ? [...sets, current] : sets

  const change = (value: string) => {
    document.cookie = value ? `qc-fixture=${value}; path=/; SameSite=Lax` : 'qc-fixture=; path=/; max-age=0'
    location.reload()
  }

  return (
    <label style={{
      display: 'grid', gap: 4, margin: 'auto 0 0 12px', padding: 8, border: '1px dashed var(--warn)',
      borderRadius: 'var(--radius)', color: 'var(--warn)', fontSize: 11,
    }}>
      <span>DEV 데이터</span>
      <select className="qc-select" style={{ width: '100%' }} value={current} onChange={(e) => change(e.target.value)}>
        <option value="">실서버 (SPT 프록시)</option>
        {options.map((s) => <option key={s} value={s}>녹화: {s}</option>)}
      </select>
    </label>
  )
}
