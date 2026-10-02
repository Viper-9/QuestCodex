// Dev tool: builds a map variant folder from tarkov.dev (09 spec) — used for the expanded Interchange that
// ManimalInterchange swaps in, since tarkov.dev already draws the live 1.x Interchange.
//   tarkov-dev-svg-maps/<Map>.svg (one file, one <g id> per floor) -> one SVG per floor, shared <style>/<defs> kept
//   tarkov-dev src/data/maps.json (bounds, floor extents)            -> map.json in the same shape as build-maps.js
//   tarkov-dev-svg-maps/LICENSE.md                                   -> copied as Shebuka-LICENSE.md
// Splitting the floors is the only change to the drawing (recorded in THIRD_PARTY_NOTICES).
// Run it after build-maps.js, which wipes the maps folder: it adds each variant folder and marks it in index.json
// (variants: { <variant id>: <folder> } on the vanilla entry), which the web picks when the server reports the mod.
// Calibration fixes found by checking dumped doors/zones against the drawing go in OVERRIDES so a rebuild keeps them.
// Usage: node build-tarkovdev-map.js <tarkov-dev-svg-maps clone> <tarkov-dev clone> [outDir]
const fs = require('fs')
const path = require('path')
const { execSync } = require('child_process')

const VARIANTS = [
  {
    out: 'interchange-manimal',
    variant: 'manimal', // server MapVariantDetector id
    baseKey: 'interchange',
    svg: 'Interchange.svg',
    tarkovDevKey: 'interchange',
    displayName: 'Interchange (Expanded, TarkovDev)',
    internalNames: ['Interchange'],
    filePrefix: 'Interchange',
  },
]

// Per-variant tweaks applied after reading tarkov.dev's numbers. Empty until a measured offset needs fixing.
const OVERRIDES = {
  'interchange-manimal': {},
}

const [svgRepo, devRepo] = process.argv.slice(2)
if (!svgRepo || !devRepo) {
  console.error('usage: node build-tarkovdev-map.js <tarkov-dev-svg-maps clone> <tarkov-dev clone> [outDir]')
  process.exit(1)
}
const outRoot = process.argv[4] ?? path.resolve(__dirname, '../../web/public/maps')

const commitOf = (dir) => {
  try {
    return execSync('git rev-parse --short HEAD', { cwd: dir }).toString().trim()
  } catch {
    return 'unknown'
  }
}

/** Top-level children of <svg>: [{ tag, id, text }] in order, plus the opening <svg …> tag. */
function topLevel(svg) {
  const open = /<svg\b[^>]*>/.exec(svg)
  const end = svg.lastIndexOf('</svg>')
  const children = []
  const re = /<(\/?)([a-zA-Z][\w:.-]*)\b[^>]*?(\/?)>/g
  re.lastIndex = open.index + open[0].length
  let depth = 0
  let start = -1
  let m
  while ((m = re.exec(svg)) && m.index < end) {
    if (m[1]) {
      if (--depth === 0) children.push(element(svg.slice(start, re.lastIndex)))
      continue
    }
    if (depth === 0) start = m.index
    if (m[3]) {
      if (depth === 0) children.push(element(svg.slice(start, re.lastIndex)))
    } else depth++
  }
  return { open: open[0], children }
}
const element = (text) => ({ tag: /^<([\w:.-]+)/.exec(text)[1], id: /\bid="([^"]*)"/.exec(text)?.[1], text })

function viewBoxOf(openTag) {
  const [, , w, h] = /viewBox="([^"]+)"/.exec(openTag)[1].trim().split(/[\s,]+/).map(Number)
  return { width: w, height: h }
}

// tarkov.dev bounds are [[x, z], [x, z]] corners in game coordinates (any order); extents bounds add a name.
const flatBounds = (pair) => ({
  min: { x: Math.min(pair[0][0], pair[1][0]), z: Math.min(pair[0][1], pair[1][1]) },
  max: { x: Math.max(pair[0][0], pair[1][0]), z: Math.max(pair[0][1], pair[1][1]) },
})
const box = (flat, [y0, y1]) => ({ min: { x: flat.min.x, y: y0, z: flat.min.z }, max: { x: flat.max.x, y: y1, z: flat.max.z } })

const maps = JSON.parse(fs.readFileSync(path.join(devRepo, 'src/data/maps.json'), 'utf8'))
const svgCommit = commitOf(svgRepo)
const devCommit = commitOf(devRepo)

for (const v of VARIANTS) {
  const entry = maps.flatMap((m) => m.maps).find((m) => m.key === v.tarkovDevKey && m.svgPath)
  if (!entry) throw new Error(`no svg map ${v.tarkovDevKey} in tarkov-dev maps.json`)
  const svg = fs.readFileSync(path.join(svgRepo, v.svg), 'utf8')
  const { open, children } = topLevel(svg)
  const shared = children.filter((c) => c.tag !== 'g')
  const viewBox = viewBoxOf(open)
  const imageBounds = flatBounds(entry.bounds)

  // Ground floor = the base svgLayer over the whole map and the map's height range; upper floors from their extents.
  const floors = [
    { name: 'Ground Floor', svgLayer: entry.svgLayer, gameBounds: [box(imageBounds, entry._heightRange ?? [-1000, 1000])] },
    ...entry.layers.filter((l) => l.svgLayer).map((l) => ({
      name: l.name,
      svgLayer: l.svgLayer,
      gameBounds: l.extents.flatMap((e) => e.bounds.map((b) => box(flatBounds(b), e.height))),
    })),
  ]

  const dir = path.join(outRoot, v.out)
  fs.rmSync(dir, { recursive: true, force: true })
  fs.mkdirSync(dir, { recursive: true })
  const layers = floors.map((f, level) => {
    const group = children.find((c) => c.tag === 'g' && c.id === f.svgLayer)
    if (!group) throw new Error(`${v.svg}: no <g id="${f.svgLayer}">`)
    const file = `${v.filePrefix}-${f.svgLayer}.svg`
    const body = [...shared, group].map((c) => `  ${c.text}`).join('\n')
    fs.writeFileSync(path.join(dir, file), `<?xml version="1.0" encoding="UTF-8"?>\n${open}\n${body}\n</svg>\n`)
    return { name: f.name, level, svg: file, viewBox, imageBounds, gameBounds: f.gameBounds }
  })

  const def = {
    displayName: v.displayName,
    internalNames: v.internalNames,
    coordinateRotation: entry.coordinateRotation,
    defaultLevel: 0,
    bounds: imageBounds,
    layers,
    attribution: {
      author: 'Shebuka',
      authorLink: 'https://github.com/the-hideout/tarkov-dev-svg-maps',
      modifiedBy: null,
      license: 'CC BY-NC-SA 4.0',
      licenseFile: 'Shebuka-LICENSE.md',
    },
    source: `tarkov-dev-svg-maps @ ${svgCommit} (${v.svg}, split per floor), tarkov-dev maps.json @ ${devCommit}`,
    ...OVERRIDES[v.out],
  }
  fs.writeFileSync(path.join(dir, 'map.json'), JSON.stringify(def, null, 2) + '\n')
  fs.copyFileSync(path.join(svgRepo, 'LICENSE.md'), path.join(dir, 'Shebuka-LICENSE.md'))
  const indexFile = path.join(outRoot, 'index.json')
  const index = JSON.parse(fs.readFileSync(indexFile, 'utf8'))
  const base = index.maps.find((m) => m.key === v.baseKey)
  if (!base) throw new Error(`index.json has no ${v.baseKey}`)
  base.variants = { ...base.variants, [v.variant]: v.out }
  fs.writeFileSync(indexFile, JSON.stringify(index, null, 2) + '\n')
  console.log(`${v.out}: ${layers.length} floors, bounds x ${imageBounds.min.x}..${imageBounds.max.x} z ${imageBounds.min.z}..${imageBounds.max.z}`)
}
