import { cls } from '../cls'
import { useT } from '../i18n/I18nContext'
import { UNKNOWN_MOD, type ModEntry } from './derive'

interface ModStripProps {
  mods: ModEntry[]                         // listMods() 결과. 비어 있으면 호출부가 렌더하지 않는다
  modColors: Record<string, number>        // assignModColors() — 목록의 모드 태그와 같은 색
  selected: ReadonlySet<string>            // 비어 있으면 '전체 모드' 가 눌린 상태
  onToggle(key: string): void
  onClear(): void
}

/** 출처 모드 칩 줄. 바닐라 상인에 퀘스트를 붙이는 모드(Icebreaker·WTT-Armory 등)를 모아 보는 용도. */
export function ModStrip({ mods, modColors, selected, onToggle, onClear }: ModStripProps) {
  const t = useT()
  const allOn = selected.size === 0
  return (
    <div className="qc-modstrip" role="group" aria-label={t('modFilter.label')}>
      <span className="qc-modstrip__label">{t('modFilter.label')}</span>
      <button type="button" className={cls('qc-chip', allOn && 'is-on')} aria-pressed={allOn} onClick={onClear}>
        {t('modFilter.all')}
      </button>
      {mods.map(({ key, count }) => {
        const on = selected.has(key)
        return (
          <button key={key} type="button" className={cls('qc-chip', on && 'is-on')} aria-pressed={on} onClick={() => onToggle(key)}>
            <span className="qc-modstrip__dot" data-mod-color={modColors[key]} />
            {key === UNKNOWN_MOD ? t('tag.mod') : key}
            <span className="qc-modstrip__count">{count}</span>
          </button>
        )
      })}
    </div>
  )
}
