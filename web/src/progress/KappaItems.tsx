import { useState } from 'react'
import type { LootSource } from '../api/catalog'
import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { distinctNames } from '../wiki/prep'
import { searchKey } from './derive'
import { CONTAINER_LABELS, formatChance, itemDone, itemSource, type KappaItem } from './kappa'

/** 주로 나오는 곳 칸에 보여 주는 컨테이너 수. 나머지는 툴팁 */
const TOP_CONTAINERS = 3

interface KappaItemsProps {
  items: KappaItem[]
  /** 카탈로그 lootSources — 구버전 서버면 undefined 이고 출처 두 열을 숨긴다 */
  sources: Record<string, LootSource> | undefined
}

/**
 * Collector 제출 아이템 표(12 kappa-loot-sources 스펙 §3) — 아이템 · 상태 · 주로 나오는 곳(컨테이너 하나 열었을 때 확률) · 봇.
 * 기본은 남은 것만. 검색어는 아이템 이름(여러 이름 중 하나라도)에 맞춘다. 상인 필터와 무관.
 */
export function KappaItems({ items, sources }: KappaItemsProps) {
  const t = useT()
  const [leftOnly, setLeftOnly] = useState(true)
  const [query, setQuery] = useState('')
  if (items.length === 0) return null
  const q = searchKey(query)
  const shown = items.filter((i) => (!leftOnly || !itemDone(i)) && (q === '' || i.items.some((x) => searchKey(x.name).includes(q))))
  const withSources = sources !== undefined
  const containerName = (c: { tpl: string; name: string }) => (CONTAINER_LABELS[c.tpl] ? t(CONTAINER_LABELS[c.tpl]) : c.name)

  return (
    <section className={cls('qc-card qc-kappa__items', withSources && 'has-sources')}>
      <div className="qc-card__bar">
        <h3 className="qc-card__h">{t('kappa.items')}</h3>
        <span className="qc-muted">{items.filter(itemDone).length} / {items.length}</span>
        <div className="qc-chips" role="group" aria-label={t('kappa.items')}>
          <button type="button" className={cls('qc-chip', leftOnly && 'is-on')} aria-pressed={leftOnly} onClick={() => setLeftOnly(true)}>{t('kappa.items.left')}</button>
          <button type="button" className={cls('qc-chip', !leftOnly && 'is-on')} aria-pressed={!leftOnly} onClick={() => setLeftOnly(false)}>{t('kappa.items.all')}</button>
        </div>
        <input className="qc-search" type="search" placeholder={t('items.search')} aria-label={t('items.search')} value={query} onChange={(e) => setQuery(e.target.value)} />
      </div>
      {shown.length === 0 ? <p className="qc-empty">{t(q === '' ? 'kappa.items.empty' : 'items.empty')}</p> : (
        <div className="qc-kappa__itable" role="table">
          <div className="qc-kappa__irow qc-kappa__irow--head" role="row">
            <span role="columnheader">{t('kappa.items.col.item')}</span>
            <span role="columnheader">{t('kappa.items.col.state')}</span>
            {withSources && <>
              <span role="columnheader" title={t('kappa.items.chanceHint')}>{t('kappa.items.col.where')}</span>
              <span role="columnheader">{t('kappa.items.col.bots')}</span>
            </>}
          </div>
          {shown.map((i) => {
            const names = distinctNames(i.items.map((x) => x.name))
            const src = withSources ? itemSource(i, sources) : null
            const containers = src?.containers ?? []
            const top = containers.slice(0, TOP_CONTAINERS)
            const allText = containers.map((c) => `${containerName(c)} ${formatChance(c.chance)}`).join('\n')
            return (
              <div key={i.conditionId} role="row" className={cls('qc-kappa__irow', itemDone(i) && 'is-done')}>
                <span className="qc-kappa__iname" title={names.join('\n')}>{names[0]}</span>
                <span><span className={cls('qc-kappa__item', `is-${i.state}`)}>{t(`kappa.item.${i.state}`)}</span></span>
                {withSources && <>
                  <span className="qc-kappa__iwhere" title={allText ? `${t('kappa.items.chanceHint')}\n\n${allText}` : undefined}>
                    {top.length === 0 ? <span className="qc-muted">—</span> : top.map((c, k) => (
                      <span key={c.tpl}>{k > 0 && ' · '}{containerName(c)} <b>{formatChance(c.chance)}</b></span>
                    ))}
                    {containers.length > TOP_CONTAINERS && <span className="qc-muted"> {t('kappa.items.more', { n: containers.length - TOP_CONTAINERS })}</span>}
                  </span>
                  <span className="qc-kappa__ibots">
                    {src && src.bots.length > 0 ? src.bots.map((b) => t(`kappa.bot.${b}`)).join(' · ') : <span className="qc-muted">—</span>}
                  </span>
                </>}
              </div>
            )
          })}
        </div>
      )}
    </section>
  )
}
