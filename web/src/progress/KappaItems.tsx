import { useState } from 'react'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { distinctNames } from '../wiki/prep'
import { itemDone, type KappaItem } from './kappa'

/** Collector 제출 아이템 — 상태 색 칩(아이콘 없음: 서버 iconUrl 이 항상 null). 상인 필터와 무관. */
export function KappaItems({ items }: { items: KappaItem[] }) {
  const t = useT()
  const [leftOnly, setLeftOnly] = useState(false)
  if (items.length === 0) return null
  const shown = leftOnly ? items.filter((i) => !itemDone(i)) : items

  return (
    <section className="qc-card qc-kappa__items">
      <div className="qc-card__bar">
        <h3 className="qc-card__h">{t('kappa.items')}</h3>
        <span className="qc-muted">{items.filter(itemDone).length} / {items.length}</span>
        <div className="qc-chips" role="group" aria-label={t('kappa.items')}>
          <button type="button" className={cls('qc-chip', !leftOnly && 'is-on')} aria-pressed={!leftOnly} onClick={() => setLeftOnly(false)}>{t('kappa.items.all')}</button>
          <button type="button" className={cls('qc-chip', leftOnly && 'is-on')} aria-pressed={leftOnly} onClick={() => setLeftOnly(true)}>{t('kappa.items.left')}</button>
        </div>
      </div>
      {shown.length === 0 ? <p className="qc-empty">{t('kappa.items.empty')}</p> : (
        <ul className="qc-kappa__chips">
          {shown.map((i) => {
            const names = distinctNames(i.items.map((x) => x.name))
            return (
              <li key={i.conditionId} className={cls('qc-kappa__item', `is-${i.state}`)} title={`${names.join('\n')}\n${t(`kappa.item.${i.state}`)}`}>
                {(i.state === 'given' || i.state === 'ready') && '✓ '}
                {names[0]}
                {i.state === 'nonFir' && <span className="qc-kappa__itemnote"> · {t('kappa.item.nonFir')}</span>}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
