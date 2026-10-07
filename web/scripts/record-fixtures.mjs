// 녹화 데이터 만들기(dev-docs/ops/dev-fixtures.runbook.md). 지금 떠 있는 SPT 서버의 API 응답을 web/fixtures/<세트>/ 에 저장한다.
//   npm run record-fixtures -- vanilla
// 저장하는 것: 프로필 목록, 캐릭터가 있는 프로필마다 진행 데이터, UI 언어(en·kr·ru)별 카탈로그.
// SPT 4.1.5 는 자체 서명 인증서로 https 만 서빙하므로 이 프로세스에서만 인증서 검사를 끈다.
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'

const SERVER = process.env.QC_SERVER ?? 'https://127.0.0.1:6969'
const LANGS = ['en', 'kr', 'ru']
const set = process.argv[2]
if (!set || !/^[\w-]+$/.test(set)) {
  console.error('usage: npm run record-fixtures -- <set>   (e.g. vanilla)')
  process.exit(1)
}

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'fixtures', set)
fs.rmSync(root, { recursive: true, force: true })

async function save(api, rel) {
  const res = await fetch(SERVER + api)
  if (!res.ok) throw new Error(`${api} → HTTP ${res.status}`)
  const text = await res.text()
  const file = path.join(root, rel)
  fs.mkdirSync(path.dirname(file), { recursive: true })
  fs.writeFileSync(file, text)
  console.log(`saved ${rel} (${(text.length / 1024).toFixed(0)} KB)`)
  return JSON.parse(text)
}

const profiles = await save('/questcodex/api/profiles', 'profiles.json')
for (const p of profiles) {
  if (!p.hasCharacter) continue
  await save(`/questcodex/api/profiles/${encodeURIComponent(p.id)}/progress`, `progress/${p.id}.json`)
}
for (const lang of LANGS) await save(`/questcodex/api/catalog?lang=${lang}`, `catalog/${lang}.json`)
console.log(`done: ${root}`)
