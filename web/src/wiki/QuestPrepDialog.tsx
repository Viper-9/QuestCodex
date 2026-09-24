import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { CatalogQuest, Objective, ObjectivePrep } from '../api/catalog'
import { useT } from '../i18n/I18nContext'
import { formatInt, formatObjective, lineText } from './format'
import { useDialogFrame } from './useDialogFrame'
import { distinctNames, exitText, hasPrep, hasRules, headerMap, itemDetails, optionText } from './prep'

interface QuestPrepDialogProps {
  /** null 이면 닫힘. QuestDescriptionDialog 와 같은 showModal()/close() 토글. */
  quest: CatalogQuest | null
  traderName: string
  onClose(): void
}

type Prepped = Objective & { prep: ObjectivePrep }

/** 준비물 팝업. 위: 챙길 아이템(제출·설치) 모음, 아래: 목표별 무기·장비·레이드 조건. */
export function QuestPrepDialog({ quest, traderName, onClose }: QuestPrepDialogProps) {
  const t = useT()
  const ref = useRef<HTMLDialogElement>(null)
  const frame = useDialogFrame(ref, 'prep', quest !== null, onClose)

  useEffect(() => {
    const el = ref.current
    if (!el) return
    if (quest && !el.open) el.showModal()
    else if (!quest && el.open) el.close()
  }, [quest])

  const prepped: Prepped[] = (quest?.objectives ?? []).filter(hasPrep)
  const withItem = prepped.filter((o) => o.prep.item !== null)
  const withRules = prepped.filter((o) => hasRules(o.prep))
  const mapText = quest ? headerMap(quest, t) : null

  return (
    <dialog
      ref={ref}
      className="qc-dialog qc-dialog--wide"
      onClose={onClose}
      {...frame.dialogProps}
    >
      {frame.grips}
      {quest && (
        <header className="qc-dialog__head" {...frame.headProps}>
          <button type="button" className="qc-dialog__close" aria-label={t('dialog.close')} onClick={onClose}>✕</button>
          <h3 className="qc-dialog__title">{t('prep.button')} · {quest.name}</h3>
          <p className="qc-dialog__meta">
            {traderName}
            {mapText && <> · {mapText}</>}
          </p>
        </header>
      )}
      {quest && (
        // key: 다른 퀘스트로 다시 열면 "외 N종" 펼침 상태를 초기화한다
        <div className="qc-dialog__body" key={quest.id}>

          {prepped.length === 0 && <p className="qc-detail__note">{t('prep.none')}</p>}

          {withItem.length > 0 && (
            <section className="qc-prep__sec">
              <h4 className="qc-detail__h">{t('prep.items')}</h4>
              <ul className="qc-lines">
                {withItem.map((o) => {
                  const item = o.prep.item!
                  const details = itemDetails(item, t)
                  const names = distinctNames(item.items.map((i) => i.name))
                  const single = names.length === 1
                  return (
                    <li key={o.conditionId}>
                      <div>
                        <span className="qc-prep__act">{item.action === 'plant' ? t('prep.plant') : t('prep.handover')}</span>
                        {single ? `${names[0]} ` : `${t('prep.anyOf')} `}×{formatInt(item.count)}
                        {item.foundInRaid && <span className="qc-prep__fir" title={t('prep.firHint')}>{t('prep.fir')}</span>}
                        {details.length > 0 && <span className="qc-muted"> · {details.join(' · ')}</span>}
                        {/* 대안이 여럿이면 무기 목록처럼 한 줄에 하나 (Lotus 탄약 30종·생필품 17종 같은 경우) */}
                        {!single && <Names names={names} />}
                      </div>
                    </li>
                  )
                })}
              </ul>
            </section>
          )}

          {withRules.map((o) => (
            <section key={o.conditionId} className="qc-prep__sec qc-prep__obj">
              <h4 className="qc-prep__objtitle">{lineText(formatObjective(o, t))}</h4>
              <Rules prep={o.prep} />
            </section>
          ))}
        </div>
      )}
    </dialog>
  )
}

function Rules({ prep }: { prep: ObjectivePrep }) {
  const t = useT()
  const exit = exitText(prep, t)
  const rows: [string, ReactNode][] = []
  if (prep.maps.length > 0) rows.push([t('prep.map'), prep.maps.join(' · ')])
  if (prep.weapons.length > 0) rows.push([t('prep.weapon'), <Names names={prep.weapons.map((w) => w.name)} />])
  if (prep.calibers.length > 0) rows.push([t('prep.caliber'), prep.calibers.join(' · ')])
  if (prep.weaponMods.length > 0) rows.push([t('prep.weaponMods'), <Names names={prep.weaponMods.map(optionText)} />])
  // 슬롯끼리는 AND — 슬롯마다 한 줄. 줄 안의 대안은 OR.
  prep.equipment.forEach((slot) => rows.push([t('prep.wear'), <Names names={slot.map(optionText)} />]))
  if (prep.forbiddenEquipment.length > 0) rows.push([t('prep.forbidden'), <Names names={prep.forbiddenEquipment.map((i) => i.name)} />])
  if (prep.oneRaid) rows.push([t('prep.raid'), t('prep.oneRaid')])
  if (exit !== null) rows.push([t('prep.exit'), exit])

  return (
    <dl className="qc-prep__rules">
      {rows.map(([k, v], i) => (
        <div key={i} className="qc-prep__row">
          <dt>{k}</dt>
          <dd>{v}</dd>
        </div>
      ))}
    </dl>
  )
}

const NAMES_LIMIT = 3

/** 대안 목록 — 한 줄에 하나. 길면 앞 NAMES_LIMIT 개만 보이고 "외 N종" 으로 펼친다 (발라클라바 30종 같은 경우). */
function Names({ names: raw }: { names: string[] }) {
  const t = useT()
  const [open, setOpen] = useState(false)
  const names = distinctNames(raw)
  const overflow = names.length - NAMES_LIMIT
  const shown = overflow <= 1 || open ? names : names.slice(0, NAMES_LIMIT)
  return (
    <ul className="qc-prep__names">
      {shown.map((name, i) => <li key={i}>{name}</li>)}
      {overflow > 1 && (
        <li>
          <button type="button" className={open ? 'qc-prep__more is-open' : 'qc-prep__more'} onClick={() => setOpen(!open)}>
            {open ? t('prep.less') : t('prep.more', { n: overflow })}
          </button>
        </li>
      )}
    </ul>
  )
}
