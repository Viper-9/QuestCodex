import { useState, type ReactNode } from 'react'
import type { Catalog } from '../api/catalog'
import type { ProfileProgress } from '../api/progress'
import type { NameLookup } from '../wiki/derive'
import { QuestDescriptionDialog } from '../wiki/QuestDescriptionDialog'
import { QuestMapDialog } from '../wiki/QuestMapDialog'
import { QuestPrepDialog } from '../wiki/QuestPrepDialog'
import type { KappaGraph } from './kappa'
import { KappaChainDialog } from './KappaChainDialog'

export interface KappaDialogOpeners {
  description(questId: string): void
  prep(questId: string): void
  map(questId: string): void
  chain(questId: string): void
}

/**
 * 카파 목록·트리가 같이 쓰는 팝업 4종(설명·준비·지도·연쇄). 상태는 훅 안에 두고, 여는 함수와 마운트할 노드만 돌려준다.
 * 팝업은 항상 마운트해 두고 퀘스트 id 가 null 이면 닫힌다(각 팝업의 showModal()/close() 틀).
 */
export function useKappaDialogs(
  catalog: Catalog, graph: KappaGraph, progress: ProfileProgress, done: ReadonlySet<string>, lookup: NameLookup,
): { open: KappaDialogOpeners; dialogs: ReactNode } {
  const [descriptionId, setDescriptionId] = useState<string | null>(null)
  const [prepId, setPrepId] = useState<string | null>(null)
  const [mapId, setMapId] = useState<string | null>(null)
  const [chainId, setChainId] = useState<string | null>(null)
  const quest = (id: string | null) => (id ? catalog.quests[id] ?? null : null)
  const traderName = (id: string | null) => (id ? lookup.traderName(catalog.quests[id]?.traderId ?? '') : '')

  const dialogs = <>
    <KappaChainDialog questId={chainId} catalog={catalog} graph={graph} progress={progress} done={done} lookup={lookup} onClose={() => setChainId(null)} />
    <QuestDescriptionDialog quest={quest(descriptionId)} traderName={traderName(descriptionId)} onClose={() => setDescriptionId(null)} />
    <QuestPrepDialog quest={quest(prepId)} traderName={traderName(prepId)} onClose={() => setPrepId(null)} />
    <QuestMapDialog
      quest={quest(mapId)}
      traderName={traderName(mapId)}
      lockedDoors={catalog.lockedDoors}
      exits={catalog.exits}
      mapVariants={catalog.mapVariants}
      onClose={() => setMapId(null)}
    />
  </>
  return { open: { description: setDescriptionId, prep: setPrepId, map: setMapId, chain: setChainId }, dialogs }
}
