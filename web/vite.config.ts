import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import type { Plugin } from 'vite'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// SPT 서버. 개발 중 API/이미지 요청을 여기로 프록시한다.
// SPT 4.1.5 는 자체 서명 인증서로 https 만 서빙한다 (http 는 빈 응답으로 끊김).
const SPT_SERVER = 'https://127.0.0.1:6969'
const sptProxy = { target: SPT_SERVER, secure: false }

// 녹화 데이터(dev-docs/ops/dev-fixtures.runbook.md). web/fixtures/<세트>/ 의 JSON 을 API 대신 돌려준다.
// 개발 서버 전용(apply: 'serve') — 배포 빌드에는 들어가지 않는다. 어느 세트를 쓸지는 쿠키 qc-fixture 가 정하고
// (index.html 의 전환 버튼이 바꾼다), 쿠키가 없으면 지금처럼 SPT 서버로 프록시한다.
const FIXTURE_ROOT = fileURLToPath(new URL('./fixtures', import.meta.url))

/** API 경로 → 세트 안의 파일. 모르는 경로면 null */
function fixtureFile(url: string): string | null {
  const u = new URL(url, 'http://x')
  if (u.pathname === '/questcodex/api/profiles') return 'profiles.json'
  // 프로필 ID·언어는 파일 이름이 되므로 경로 문자가 섞이면 받지 않는다
  const name = (x: string) => (/^[\w-]+$/.test(x) ? x : null)
  const progress = /^\/questcodex\/api\/profiles\/([^/]+)\/progress$/.exec(u.pathname)
  if (progress) {
    const id = name(decodeURIComponent(progress[1]))
    return id && `progress/${id}.json`
  }
  if (u.pathname === '/questcodex/api/catalog') {
    const lang = name(u.searchParams.get('lang') ?? 'en')
    return lang && `catalog/${lang}.json`
  }
  return null
}

function fixtures(): Plugin {
  return {
    name: 'questcodex-fixtures',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const url = req.url ?? ''
        if (url === '/__qc-fixtures') {
          const sets = fs.existsSync(FIXTURE_ROOT) ? fs.readdirSync(FIXTURE_ROOT).filter((d) => fs.statSync(path.join(FIXTURE_ROOT, d)).isDirectory()) : []
          res.setHeader('Content-Type', 'application/json')
          res.end(JSON.stringify(sets))
          return
        }
        const set = /(?:^|;\s*)qc-fixture=([\w-]+)/.exec(req.headers.cookie ?? '')?.[1]
        if (!set || !url.startsWith('/questcodex/api/')) return next()
        res.setHeader('Cache-Control', 'no-store')
        if (req.method === 'POST' && url.startsWith('/questcodex/api/catalog/rebuild')) {
          res.statusCode = 204
          res.end()
          return
        }
        const rel = fixtureFile(url)
        const file = rel && path.join(FIXTURE_ROOT, set, rel)
        if (!file || !fs.existsSync(file)) {
          res.statusCode = 404
          res.setHeader('Content-Type', 'application/json')
          res.end(JSON.stringify({ error: 'fixtureMissing', set, path: rel ?? url }))
          return
        }
        res.setHeader('Content-Type', 'application/json; charset=utf-8')
        fs.createReadStream(file).pipe(res)
      })
    },
  }
}

export default defineConfig({
  plugins: [react(), fixtures()],
  // ModMetadata.WWWRootUrl = "questcodex" → wwwroot/ 가 /questcodex/ 로 서빙된다.
  base: '/questcodex/',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    // Razor 호스트 페이지(server/Web/Pages/Index.razor)가 .vite/manifest.json 을 읽어
    // 해시 포함 파일명을 <script>/<link> 로 삽입한다.
    manifest: true,
    // index.html 이 아니라 main.tsx 를 엔트리로 → manifest 키가 "src/main.tsx", isEntry: true.
    // 배포본에는 index.html 이 필요 없다 (Razor 페이지가 그 역할).
    rollupOptions: {
      input: 'src/main.tsx',
      // Vite 앱 빌드 기본값(false)은 엔트리의 export 를 트리셰이킹한다. Razor 페이지가
      // JS interop 으로 mount()/unmount() 를 호출하므로 export 를 반드시 보존해야 한다.
      preserveEntrySignatures: 'strict',
    },
  },
  server: {
    proxy: {
      '/questcodex/api': sptProxy,
      '/files': sptProxy,
    },
  },
  // 순수 함수만 단위 테스트한다 (DOM 없음). 컴포넌트는 npm run dev 로 손검증.
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
})
