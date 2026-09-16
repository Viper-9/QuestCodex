import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// SPT 서버. 개발 중 API/이미지 요청을 여기로 프록시한다.
// SPT 4.1.5 는 자체 서명 인증서로 https 만 서빙한다 (http 는 빈 응답으로 끊김).
const SPT_SERVER = 'https://127.0.0.1:6969'
const sptProxy = { target: SPT_SERVER, secure: false }

export default defineConfig({
  plugins: [react()],
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
})
