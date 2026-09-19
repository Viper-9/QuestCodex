import { useState } from 'react'
import type { CatalogTrader } from '../api/catalog'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { initials } from './derive'

interface TraderStripProps {
  traders: CatalogTrader[]                 // orderTraders() 결과
  counts: Record<string, number>
  total: number
  selected: ReadonlySet<string>            // 비어 있으면 '전체' 가 눌린 상태
  onToggle(id: string): void
  onClear(): void
}

export function TraderStrip({ traders, counts, total, selected, onToggle, onClear }: TraderStripProps) {
  const t = useT()
  const allOn = selected.size === 0
  return (
    <div className="qc-traders" role="group" aria-label={t('trader.filterLabel')}>
      <button type="button" className={cls('qc-trader', allOn && 'is-on')} aria-pressed={allOn} onClick={onClear}>
        <span className="qc-trader__avatar">{t('trader.all')}</span>
        <span className="qc-trader__name">{t('trader.all')}</span>
        <span className="qc-trader__count">{total}</span>
      </button>
      {traders.map((tr) => {
        const on = selected.has(tr.id)
        return (
          <button
            key={tr.id}
            type="button"
            className={cls('qc-trader', on && 'is-on', !tr.isVanilla && 'is-mod')}
            aria-pressed={on}
            onClick={() => onToggle(tr.id)}
          >
            <TraderAvatar trader={tr} />
            <span className="qc-trader__name">{tr.name}</span>
            <span className="qc-trader__count">{counts[tr.id] ?? 0}{!tr.isVanilla && t('trader.modSuffix')}</span>
          </button>
        )
      })}
    </div>
  )
}

/** avatarUrl 이 null 이거나 로드 실패면 이니셜 플레이스홀더. */
function TraderAvatar({ trader }: { trader: CatalogTrader }) {
  const [broken, setBroken] = useState(false)
  if (!trader.avatarUrl || broken) {
    return <span className="qc-trader__avatar">{initials(trader.name)}</span>
  }
  return <img className="qc-trader__avatar" src={trader.avatarUrl} alt="" onError={() => setBroken(true)} />
}
