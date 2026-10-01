import { useMemo } from 'react'
import type { Catalog } from '../api/catalog'
import { useT } from '../i18n/I18nContext'
import type { Route } from '../shell/router'
import { makeLookup } from '../wiki/derive'
import { ItemsView } from './ItemsView'
import { OverviewView } from './OverviewView'
import { ProfileBar } from './ProfileBar'
import { RaidView } from './RaidView'
import { UnlocksView } from './UnlocksView'
import { useProgress } from './useProgress'
import '../wiki/wiki.css'   // 검색창·칩·태그·목록 줄 같은 공용 규칙을 위키와 같이 쓴다
import './progress.css'

interface ProgressPageProps {
  catalog: Catalog | null
  route: Route
}

/** 진행현황 그룹의 껍데기: 프로필 바(한 번) + 소메뉴 화면. 프로필·진행 상태는 여기서만 소유한다. */
export function ProgressPage({ catalog, route }: ProgressPageProps) {
  const t = useT()
  const p = useProgress()
  const lookup = useMemo(() => (catalog ? makeLookup(catalog) : null), [catalog])
  /** 보유 수 이전 서버(필드 없음)면 빈 표 — 화면은 그대로 쓰고 안내만 띄운다 */
  const inventory = useMemo(() => p.progress?.inventory ?? {}, [p.progress])

  return (
    <div className="qc-progress">
      <ProfileBar
        profiles={p.profiles ?? []}
        profileId={p.profileId}
        onSelect={p.setProfileId}
        progress={p.progress}
        loading={p.loading}
        updatedAt={p.updatedAt}
        onRefresh={p.refresh}
      />
      {p.error && (
        <div className="qc-error" role="alert">
          <span>{t('error.progress', { code: p.error })}</span>
          <button type="button" className="qc-btn" onClick={p.retry}>{t('error.retry')}</button>
        </div>
      )}
      {p.profiles?.length === 0 && <p className="qc-empty">{t('profile.none')}</p>}
      {p.progress && p.progress.inventory === undefined && <p className="qc-progress__notice">{t('profile.noInventory')}</p>}

      {catalog && lookup && p.progress ? (
        <>
          {route.sub === 'overview' && (
            <OverviewView catalog={catalog} progress={p.progress} inventory={inventory} lookup={lookup} highlight={p.highlight} />
          )}
          {route.sub === 'raid' && (
            <RaidView catalog={catalog} progress={p.progress} inventory={inventory} lookup={lookup} map={route.query.get('map')} />
          )}
          {route.sub === 'items' && (
            <ItemsView catalog={catalog} progress={p.progress} inventory={inventory} lookup={lookup} />
          )}
          {route.sub === 'unlocks' && <UnlocksView catalog={catalog} progress={p.progress} lookup={lookup} />}
        </>
      ) : (
        !p.error && p.profiles?.length !== 0 && <p className="qc-empty">{t('app.loading')}</p>
      )}
    </div>
  )
}
