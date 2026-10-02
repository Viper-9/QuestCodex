// Dev tool: builds web/public/maps/icebreaker (11 spec) from re3mr's 2D Icebreaker poster.
//   poster (one 7680x4320 JPG, one panel per deck) -> one SVG per deck, traced from a fixed palette
//   CALIBRATION + deck heights                      -> map.json in the same shape as build-maps.js
//   re3mr-LICENSE.md                                -> CC BY-NC-SA 4.0 legal code (copied from another map folder)
// The poster is CC BY-NC-SA 4.0 (re3mr, https://reemr.se/), so the traced SVGs are too. QuestCodex crops each deck
// panel, drops the icons, labels and room names, and traces the rest; recorded in THIRD_PARTY_NOTICES.
// Run it after build-maps.js, which wipes the maps folder; it adds the icebreaker entry to index.json.
// Usage: npm install (once, in tools/maps), then
//   node build-icebreaker-map.js <icebreaker-2d.jpg> [outDir]          build
//   node build-icebreaker-map.js <icebreaker-2d.jpg> --check <dir>     also write overlay PNGs of the mod's loot,
//                                                                      the dumped doors and zones (calibration check)
const fs = require('fs')
const path = require('path')
const crypto = require('crypto')
const jpeg = require('jpeg-js')
const ImageTracer = require('imagetracerjs')

// https://tarkov.dev/maps/icebreaker-2d.jpg, "VERSION 0.8" by re3mr. Not committed; a different file fails here.
const POSTER_SHA256 = '33a8c92d082a3d07a1c974c72b2392f62f88ebdc130f07c3a0443cfd270e924a'

// Deck panels, left to right on the poster (x = left edge of the panel's hull silhouette, poster pixels).
// Heights (Unity y) are the deck's range, about half a metre under its floor up to the next deck's. Floors measured
// with --check: the height bins of the mod's loot that land on the ship on only one panel (11 spec §1).
const DECKS = [
  { level: 10, name: 'Bridge Roof', x: 52, y: [41.9, 1000] },
  { level: 9, name: 'Bridge', x: 596, y: [39.0, 41.9] },
  { level: 8, name: 'Stairs (Blocked)', x: 1136, y: [36.6, 39.0] },
  { level: 7, name: "Officers' Deck", x: 1680, y: [34.0, 36.6] },
  { level: 6, name: 'Accommodation (Upper)', x: 2222, y: [31.4, 34.0] },
  { level: 5, name: 'Accommodation (Mid)', x: 2770, y: [28.6, 31.4] },
  { level: 4, name: 'Accommodation (Lower)', x: 3314, y: [25.8, 28.6] },
  { level: 3, name: 'Gym & Canteen', x: 3858, y: [23.0, 25.8] },
  { level: 2, name: 'Helipad', x: 4404, y: [20.9, 23.0] },
  { level: 1, name: 'Infirmary', x: 4946, y: [17.9, 20.9] },
  { level: 0, name: 'Storage & Security', x: 5492, y: [15.0, 17.9] },
  { level: -1, name: 'Fuel Pumps', x: 6036, y: [9.5, 15.0] },
  { level: -2, name: 'Engine Room', x: 6580, y: [3.5, 9.5] },
  { level: -3, name: 'Lower Automation', x: 7124, y: [-1000, 3.5] },
]

// Poster furniture inside a crop, in crop pixels [x0, y0, x1, y1], painted as void: the title and version box over the
// tops of the left panels, the compass over the last one.
const ERASE = {
  10: [[0, 0, 522, 420], [100, 1300, 420, 1600]], 9: [[0, 0, 522, 420]], 8: [[0, 0, 522, 420]], 7: [[0, 0, 522, 420]],
  6: [[0, 0, 522, 420]], 5: [[0, 0, 522, 420]], 4: [[0, 0, 522, 420]],
  [-3]: [[0, 0, 522, 420]],
}
// Icons have black plates and outlines (wall) and dark shading (void): pixels of those colours this close to an icon
// colour are refilled with the icon. Void and grey shading grow less, since real openings and rails border the decks.
const ICON_BLACK_PX = 12
const ICON_SHADE_PX = 4
// Walls are 1-4 px lines. Black areas that contain a solid square this wide are icons (scorpions, rogue badges, arrows)
// and are refilled together with BLOB_MARGIN_PX of black around them.
const BLOB_CORE_PX = 5
const BLOB_MARGIN_PX = 4
// Connected pieces smaller than this (room names, deck numbers, stair treads, specks) are refilled from around them.
const MIN_PIECE_PX = 260

// Crop: the panel's hull silhouette is ~509 px wide and starts at y = 130. The bottom stops above the PMC spawn rings
// and deck names; every deck's drawing ends before it.
const CROP = { left: -6, width: 522, top: 120, bottom: 2340 }

// Poster pixels <-> Unity metres (coordinateRotation 180: Unity x grows to the left, z downwards).
// scale = px per metre, centerX = crop column of Unity x = 0, topZ = Unity z at the crop's top edge.
// Fitted with --check against the mod's looseLoot and the dumped doors (11 spec §1).
const CALIBRATION = { scale: 14.97, centerX: 260, topZ: -87.0 }
// Per-deck nudges in crop pixels ({ dx, dy }) when one panel sits off the others. Empty until a check needs one.
const OVERRIDES = {}

// Palette the panels are drawn in. null = not drawn (outside the ship, and the dark "ghost" hull / open voids, so the
// decks below show through like every other layered map).
const PALETTE = [
  { name: 'outside', rgb: [0x18, 0x18, 0x18], fill: null },
  { name: 'void', rgb: [0x20, 0x28, 0x34], fill: null },
  { name: 'deck', rgb: [0x60, 0x70, 0x88], fill: '#607088' },
  { name: 'room', rgb: [0x8c, 0xa0, 0xbc], fill: '#8ca0bc' },
  { name: 'wall', rgb: [0x06, 0x08, 0x0c], fill: '#06080c' },
  { name: 'rail', rgb: [0x88, 0x88, 0x90], fill: '#888890' },
]

const args = process.argv.slice(2)
const poster = args[0]
const checkAt = args.indexOf('--check')
const checkDir = checkAt >= 0 ? args[checkAt + 1] : null
const outRoot = args.find((a, i) => i > 0 && a !== '--check' && i !== checkAt + 1) ?? path.resolve(__dirname, '../../web/public/maps')
if (!poster) {
  console.error('usage: node build-icebreaker-map.js <icebreaker-2d.jpg> [outDir] [--check <dir>]')
  process.exit(1)
}

const bytes = fs.readFileSync(poster)
const sha = crypto.createHash('sha256').update(bytes).digest('hex')
if (sha !== POSTER_SHA256) throw new Error(`${poster}: sha256 ${sha}, expected ${POSTER_SHA256}`)
const img = jpeg.decode(bytes, { maxMemoryUsageInMB: 1024 })

const W = CROP.width
const H = CROP.bottom - CROP.top

// Hue in degrees and saturation (max - min) of an RGB triple.
function hueSat([r, g, b]) {
  const max = Math.max(r, g, b)
  const min = Math.min(r, g, b)
  const d = max - min
  if (d === 0) return [0, 0]
  let h
  if (max === r) h = ((g - b) / d) % 6
  else if (max === g) h = (b - r) / d + 2
  else h = (r - g) / d + 4
  return [(h * 60 + 360) % 360, d]
}

// Palette index of a pixel, or -1 for icon colours (saturated, not the poster's blue-grey) and pure white
// (labels, glows, stair treads), which get filled from their neighbours.
function classify(rgb) {
  const [h, s] = hueSat(rgb)
  if (s > 30 && (h < 190 || h > 250)) return -1
  if (Math.min(...rgb) > 0xb0) return -1
  let best = 0
  let bestD = Infinity
  PALETTE.forEach((p, i) => {
    const d = (p.rgb[0] - rgb[0]) ** 2 + (p.rgb[1] - rgb[1]) ** 2 + (p.rgb[2] - rgb[2]) ** 2
    if (d < bestD) { bestD = d; best = i }
  })
  // Anti-aliased text in rooms sits between room and white; treat anything brighter than a room as a room.
  if (Math.min(...rgb) > 0x98) return PALETTE.findIndex((p) => p.name === 'room')
  return best
}

// Crop one deck panel into palette indices.
function panelIndices(deck) {
  const x0 = deck.x + CROP.left
  const idx = new Int8Array(W * H)
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const i = ((CROP.top + y) * img.width + x0 + x) * 4
      idx[y * W + x] = classify([img.data[i], img.data[i + 1], img.data[i + 2]])
    }
  }
  const voidIdx = PALETTE.findIndex((p) => p.name === 'void')
  for (const [ex0, ey0, ex1, ey1] of ERASE[deck.level] ?? []) {
    for (let y = ey0; y < Math.min(ey1, H); y++) for (let x = ex0; x < Math.min(ex1, W); x++) idx[y * W + x] = voidIdx
  }
  dropEnclosedOutside(idx)
  dropBlackBlobs(idx)
  growIconsOverOutlines(idx)
  fillUnknown(idx)
  dropSmallPieces(idx)
  fillUnknown(idx)
  return modeFilter(idx)
}

// The poster background only surrounds the panels. Near-black wall lines and icon plates (the WEDGE boss tag) are
// closer to it than to the wall colour after JPEG, so background not connected to the crop's edge is wall; the icon
// steps below then remove the plates.
function dropEnclosedOutside(idx) {
  const outside = PALETTE.findIndex((p) => p.name === 'outside')
  const reached = new Uint8Array(W * H)
  const stack = []
  for (let x = 0; x < W; x++) stack.push(x, (H - 1) * W + x)
  for (let y = 0; y < H; y++) stack.push(y * W, y * W + W - 1)
  while (stack.length) {
    const i = stack.pop()
    if (reached[i] || idx[i] !== outside) continue
    reached[i] = 1
    const x = i % W
    if (x > 0) stack.push(i - 1)
    if (x < W - 1) stack.push(i + 1)
    if (i >= W) stack.push(i - W)
    if (i < W * H - W) stack.push(i + W)
  }
  const wall = PALETTE.findIndex((p) => p.name === 'wall')
  idx.forEach((v, i) => { if (v === outside && !reached[i]) idx[i] = wall })
}

function dropBlackBlobs(idx) {
  const wall = PALETTE.findIndex((p) => p.name === 'wall')
  const r = Math.floor(BLOB_CORE_PX / 2)
  const blob = new Uint8Array(W * H)
  for (let y = r; y < H - r; y++) {
    for (let x = r; x < W - r; x++) {
      let solid = true
      for (let dy = -r; dy <= r && solid; dy++) for (let dx = -r; dx <= r && solid; dx++) solid = idx[(y + dy) * W + x + dx] === wall
      if (!solid) continue
      for (let dy = -r; dy <= r; dy++) for (let dx = -r; dx <= r; dx++) blob[(y + dy) * W + x + dx] = 1
    }
  }
  // Grow over the surrounding black so the blob's antialiased rim goes too.
  for (let pass = 0; pass < BLOB_MARGIN_PX; pass++) {
    const grow = []
    for (let i = W; i < W * H - W; i++) {
      if (blob[i] || idx[i] !== wall) continue
      if (blob[i - 1] || blob[i + 1] || blob[i - W] || blob[i + W]) grow.push(i)
    }
    for (const i of grow) blob[i] = 1
  }
  blob.forEach((b, i) => { if (b) idx[i] = -1 })
}

function growIconsOverOutlines(idx) {
  const dark = new Set(['wall', 'void', 'rail'].map((n) => PALETTE.findIndex((p) => p.name === n)))
  const wall = PALETTE.findIndex((p) => p.name === 'wall')
  for (let pass = 0; pass < ICON_BLACK_PX; pass++) {
    const grow = []
    for (let y = 1; y < H - 1; y++) {
      for (let x = 1; x < W - 1; x++) {
        const i = y * W + x
        if (!dark.has(idx[i]) || (pass >= ICON_SHADE_PX && idx[i] !== wall)) continue
        if (idx[i - 1] === -1 || idx[i + 1] === -1 || idx[i - W] === -1 || idx[i + W] === -1) grow.push(i)
      }
    }
    for (const i of grow) idx[i] = -1
  }
}

// Marks every 4-connected piece of one colour smaller than MIN_PIECE_PX as unknown.
function dropSmallPieces(idx) {
  const seen = new Uint8Array(W * H)
  const stack = []
  for (let start = 0; start < W * H; start++) {
    if (seen[start] || idx[start] < 0) continue
    const colour = idx[start]
    const piece = []
    stack.push(start)
    seen[start] = 1
    while (stack.length) {
      const i = stack.pop()
      piece.push(i)
      const x = i % W
      for (const n of [x > 0 ? i - 1 : -1, x < W - 1 ? i + 1 : -1, i - W, i + W]) {
        if (n < 0 || n >= W * H || seen[n] || idx[n] !== colour) continue
        seen[n] = 1
        stack.push(n)
      }
    }
    if (piece.length < MIN_PIECE_PX) for (const i of piece) idx[i] = -1
  }
}

// Grow known colours into unknown pixels (icons, labels) until none are left: each pass takes the most common known
// neighbour, so an icon on a deck becomes deck and one straddling a wall is split along it.
function fillUnknown(idx) {
  for (let pass = 0; pass < 200; pass++) {
    const next = idx.slice()
    let left = 0
    for (let y = 0; y < H; y++) {
      for (let x = 0; x < W; x++) {
        if (idx[y * W + x] !== -1) continue
        const votes = new Map()
        for (let dy = -1; dy <= 1; dy++) {
          for (let dx = -1; dx <= 1; dx++) {
            const nx = x + dx
            const ny = y + dy
            if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue
            const v = idx[ny * W + nx]
            if (v >= 0) votes.set(v, (votes.get(v) ?? 0) + 1)
          }
        }
        if (votes.size === 0) { left++; continue }
        next[y * W + x] = [...votes].sort((a, b) => b[1] - a[1])[0][0]
      }
    }
    idx.set(next)
    if (left === 0) return
  }
  idx.forEach((v, i) => { if (v === -1) idx[i] = 0 })
}

// 3x3 majority filter: removes JPEG speckle so the tracer does not emit thousands of one-pixel paths.
function modeFilter(idx) {
  const out = idx.slice()
  const counts = new Int32Array(PALETTE.length)
  for (let y = 1; y < H - 1; y++) {
    for (let x = 1; x < W - 1; x++) {
      counts.fill(0)
      for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) counts[idx[(y + dy) * W + x + dx]]++
      let best = idx[y * W + x]
      for (let c = 0; c < counts.length; c++) if (counts[c] > counts[best]) best = c
      if (counts[best] >= 5) out[y * W + x] = best
    }
  }
  return out
}

// Trace palette indices into one SVG. The tracer works on RGBA, so paint each index with its palette colour first.
function traceSvg(idx) {
  const data = new Uint8ClampedArray(W * H * 4)
  idx.forEach((v, i) => {
    const [r, g, b] = PALETTE[v].rgb
    data.set([r, g, b, 255], i * 4)
  })
  const traced = ImageTracer.imagedataToTracedata({ width: W, height: H, data }, {
    pal: PALETTE.map((p) => ({ r: p.rgb[0], g: p.rgb[1], b: p.rgb[2], a: 255 })),
    colorsampling: 0, numberofcolors: PALETTE.length, colorquantcycles: 1, mincolorratio: 0,
    ltres: 1, qtres: 1, pathomit: 12, rightangleenhance: true, blurradius: 0, roundcoords: 1, linefilter: true,
  })
  const paths = []
  // Draw order = palette order (deck under rooms under walls), so holes in one layer are covered by the next.
  traced.layers.forEach((layer, li) => {
    const fill = PALETTE[li].fill
    if (!fill) return
    let d = ''
    for (const seg of layer) {
      if (seg.isholepath) continue
      d += pathData(seg)
      for (const h of seg.holechildren) d += pathData(layer[h])
    }
    if (d) paths.push(`  <path fill="${fill}" fill-rule="evenodd" d="${d.trim()}"/>`)
  })
  return `<?xml version="1.0" encoding="UTF-8"?>\n<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">\n${paths.join('\n')}\n</svg>\n`
}

const r1 = (v) => Math.round(v * 10) / 10
function pathData(seg) {
  const s = seg.segments
  let d = `M${r1(s[0].x1)} ${r1(s[0].y1)}`
  for (const p of s) d += p.type === 'L' ? `L${r1(p.x2)} ${r1(p.y2)}` : `Q${r1(p.x2)} ${r1(p.y2)} ${r1(p.x3)} ${r1(p.y3)}`
  return d + 'Z '
}

// Unity metres of the crop's edges (see CALIBRATION).
function imageBoundsOf(deck) {
  const o = OVERRIDES[deck.level] ?? {}
  const cx = CALIBRATION.centerX + (o.dx ?? 0)
  const top = CALIBRATION.topZ - (o.dy ?? 0) / CALIBRATION.scale
  const s = CALIBRATION.scale
  const r = (v) => Math.round(v * 100) / 100
  return { min: { x: r((cx - W) / s), z: r(top) }, max: { x: r(cx / s), z: r(top + H / s) } }
}

// Unity (x, z) -> crop pixel, the inverse of imageBoundsOf with rotation 180.
function toPixel(deck, p) {
  const b = imageBoundsOf(deck)
  return { x: ((b.max.x - p.x) / (b.max.x - b.min.x)) * W, y: ((p.z - b.min.z) / (b.max.z - b.min.z)) * H }
}

const out = path.join(outRoot, 'icebreaker')
fs.rmSync(out, { recursive: true, force: true })
fs.mkdirSync(out, { recursive: true })

const panels = new Map()
const layers = DECKS.map((deck) => {
  const idx = panelIndices(deck)
  panels.set(deck.level, idx)
  const file = `Icebreaker-Deck_${deck.level < 0 ? 'm' + -deck.level : deck.level}.svg`
  fs.writeFileSync(path.join(out, file), traceSvg(idx))
  const bounds = imageBoundsOf(deck)
  console.log(`deck ${deck.level} ${deck.name}: ${file} ${(fs.statSync(path.join(out, file)).size / 1024).toFixed(0)} KB`)
  return {
    name: deck.name,
    level: deck.level,
    svg: file,
    viewBox: { width: W, height: H },
    imageBounds: bounds,
    gameBounds: [{ min: { x: bounds.min.x, y: deck.y[0], z: bounds.min.z }, max: { x: bounds.max.x, y: deck.y[1], z: bounds.max.z } }],
  }
}).sort((a, b) => a.level - b.level)

const bounds = layers[0].imageBounds
const def = {
  displayName: 'Icebreaker',
  internalNames: ['icebreaker'],
  coordinateRotation: 180,
  defaultLevel: 1,
  floorNames: 'deck', // floor buttons read "Deck n", the poster's deck numbers
  bounds,
  layers,
  attribution: {
    author: 're3mr',
    authorLink: 'https://reemr.se/',
    modifiedBy: 'Viper-9',
    license: 'CC BY-NC-SA 4.0',
    licenseFile: 're3mr-LICENSE.md',
    calibration: null, // fitted here from the mod's loot and dumped doors, so no third-party calibration to credit
  },
  source: `re3mr Icebreaker 2D map v0.8 (tarkov.dev/maps/icebreaker-2d.jpg, sha256 ${sha.slice(0, 12)}), decks traced by QuestCodex`,
}
fs.writeFileSync(path.join(out, 'map.json'), JSON.stringify(def, null, 2) + '\n')
fs.copyFileSync(path.join(outRoot, 'interchange-manimal', 'Shebuka-LICENSE.md'), path.join(out, 're3mr-LICENSE.md'))

const indexFile = path.join(outRoot, 'index.json')
const index = JSON.parse(fs.readFileSync(indexFile, 'utf8'))
index.maps = index.maps.filter((m) => m.key !== 'icebreaker')
index.maps.push({ key: 'icebreaker', internalNames: ['icebreaker'] })
fs.writeFileSync(indexFile, JSON.stringify(index, null, 2) + '\n')
console.log(`wrote ${out} and the index entry`)

if (checkDir) writeChecks(checkDir)

// Overlay PNGs: each deck's palette crop with the points whose height falls in that deck. Red = mod looseLoot,
// yellow = dumped doors, cyan = dumped zones / exits. Also prints how many points land on the ship per deck.
function writeChecks(dir) {
  const { PNG } = require('pngjs')
  const repo = path.resolve(__dirname, '../..')
  const mods = process.env.SPT_MODS ?? 'F:/SPT4.1.2/SPT_Runtime/user/mods'
  const loot = JSON.parse(fs.readFileSync(path.join(mods, 'ManimalIcebreaker/db/looseLoot.json'), 'utf8'))
    .spawnpoints.map((s) => ({ ...s.template.Position, kind: 'loot' }))
  const dump = JSON.parse(fs.readFileSync(path.join(repo, 'tools/zone-dump/dumps/icebreaker.json'), 'utf8'))
  const P = (p, kind) => ({ x: p.X, y: p.Y, z: p.Z, kind })
  const points = [
    ...loot,
    ...dump.Doors.map((d) => P(d.Position, 'door')),
    ...dump.Zones.map((z) => P(z.Position, 'zone')),
    ...(dump.Named ?? []).map((n) => P(n.Position, 'door')),
  ]
  const colors = { loot: [255, 40, 40], door: [255, 220, 0], zone: [0, 230, 255] }
  fs.mkdirSync(dir, { recursive: true })
  for (const deck of DECKS) {
    const idx = panels.get(deck.level)
    const png = new PNG({ width: W, height: H })
    idx.forEach((v, i) => png.data.set([...PALETTE[v].rgb, 255], i * 4))
    let on = 0
    let total = 0
    for (const p of points.filter((q) => q.y >= deck.y[0] && q.y < deck.y[1])) {
      const { x, y } = toPixel(deck, p)
      total++
      const v = idx[Math.round(y) * W + Math.round(x)]
      if (v !== undefined && PALETTE[v].fill) on++
      for (let dy = -3; dy <= 3; dy++) {
        for (let dx = -3; dx <= 3; dx++) {
          const px = Math.round(x) + dx
          const py = Math.round(y) + dy
          if (px < 0 || py < 0 || px >= W || py >= H) continue
          png.data.set([...colors[p.kind], 255], (py * W + px) * 4)
        }
      }
    }
    fs.writeFileSync(path.join(dir, `deck_${deck.level}.png`), PNG.sync.write(png))
    console.log(`check deck ${deck.level}: ${on}/${total} points on the ship`)
  }
}
