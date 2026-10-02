// Dev tool: converts DynamicMaps map definitions into the web's map assets.
//   <DynamicMaps>/Plugin/Resources/Maps/<Map>/<Map>.jsonc + Layers/*.svg + license
//   -> web/public/maps/<mapKey>/map.json + the layer SVGs and license copied unmodified
//   -> web/public/maps/index.json (internal name -> folder)
// Source: git clone --depth 1 https://github.com/acidphantasm/SPT-DynamicMaps.git (checked at 4944764)
// Usage: node build-maps.js <DynamicMaps clone dir> [outDir]
const fs = require('fs')
const path = require('path')
const { execSync } = require('child_process')

const repo = process.argv[2]
if (!repo) {
  console.error('usage: node build-maps.js <DynamicMaps clone dir> [outDir]')
  process.exit(1)
}
const mapsDir = path.join(repo, 'Plugin/Resources/Maps')
const outDir = process.argv[3] ?? path.resolve(__dirname, '../../web/public/maps')

let commit = 'unknown'
try {
  commit = execSync('git rev-parse --short HEAD', { cwd: repo }).toString().trim()
} catch {}

// Drops // comments (outside strings) and trailing commas, which is all the DynamicMaps files use.
function parseJsonc(text) {
  let out = ''
  let inString = false
  for (let i = 0; i < text.length; i++) {
    const c = text[i]
    if (inString) {
      out += c
      if (c === '\\') out += text[++i]
      else if (c === '"') inString = false
    } else if (c === '"') {
      inString = true
      out += c
    } else if (c === '/' && text[i + 1] === '/') {
      while (i < text.length && text[i] !== '\n') i++
      out += '\n'
    } else {
      out += c
    }
  }
  return JSON.parse(out.replace(/,(\s*[}\]])/g, '$1'))
}

function viewBoxOf(svgFile) {
  const m = /viewBox="([^"]+)"/.exec(fs.readFileSync(svgFile, 'utf8'))
  if (!m) throw new Error(`no viewBox in ${svgFile}`)
  const [, , w, h] = m[1].trim().split(/[\s,]+/).map(Number)
  return { width: w, height: h }
}

// The license file name tells who drew the map and who modified it (see map_and_data_credits.txt).
function attributionOf(licenseFile, jsonc) {
  const modified = /MODIFIED BY (.+)\.md$/i.exec(licenseFile)
  const original = licenseFile.toLowerCase().startsWith('shebuka') ? 'Shebuka' : licenseFile.split('-')[0]
  return {
    author: original,
    authorLink: jsonc.AuthorLink,
    modifiedBy: modified ? modified[1].toLowerCase() : null,
    license: 'CC BY-NC-SA 4.0',
    licenseFile,
  }
}

// DynamicMaps stores 2D points as (x, y) = Unity (x, z) and 3D bounds as (x, y, z) = Unity (x, z, height).
// Re-key everything to Unity names here so the web never has to remember the swap.
const flat = (p) => ({ x: p.x, z: p.y })
const unity = (p) => ({ x: p.x, y: p.z, z: p.y })

fs.rmSync(outDir, { recursive: true, force: true })
fs.mkdirSync(outDir, { recursive: true })
const index = []

for (const folder of fs.readdirSync(mapsDir).sort()) {
  const dir = path.join(mapsDir, folder)
  if (!fs.statSync(dir).isDirectory()) continue
  const jsoncFile = fs.readdirSync(dir).find((f) => f.endsWith('.jsonc'))
  const jsonc = parseJsonc(fs.readFileSync(path.join(dir, jsoncFile), 'utf8'))
  const layersDir = path.join(dir, 'Layers')
  const licenseFile = fs.readdirSync(layersDir).find((f) => /license/i.test(f))

  const key = jsonc.MapInternalNames[0].toLowerCase()
  const target = path.join(outDir, key)
  fs.mkdirSync(target, { recursive: true })
  fs.copyFileSync(path.join(layersDir, licenseFile), path.join(target, licenseFile))

  const layers = Object.entries(jsonc.Layers).map(([name, layer]) => {
    const svg = path.basename(layer.ImagePath)
    fs.copyFileSync(path.join(layersDir, svg), path.join(target, svg))
    return {
      name,
      level: layer.Level,
      svg,
      viewBox: viewBoxOf(path.join(layersDir, svg)),
      imageBounds: { min: flat(layer.ImageBounds.Min), max: flat(layer.ImageBounds.Max) },
      gameBounds: layer.GameBounds.map((b) => ({ min: unity(b.Min), max: unity(b.Max) })),
    }
  })
  layers.sort((a, b) => a.level - b.level)

  const map = {
    displayName: jsonc.DisplayName,
    internalNames: jsonc.MapInternalNames,
    coordinateRotation: jsonc.CoordinateRotation,
    defaultLevel: jsonc.DefaultLevel,
    bounds: { min: flat(jsonc.Bounds.Min), max: flat(jsonc.Bounds.Max) },
    layers,
    attribution: attributionOf(licenseFile, jsonc),
    source: `DynamicMaps (MIT, acidphantasm) ${folder} @ ${commit}`,
  }
  fs.writeFileSync(path.join(target, 'map.json'), JSON.stringify(map, null, 2) + '\n')
  index.push({ key, internalNames: jsonc.MapInternalNames })
  console.log(`${key}: ${layers.length} layers, ${map.attribution.author}${map.attribution.modifiedBy ? ` / ${map.attribution.modifiedBy}` : ''}`)
}

fs.writeFileSync(path.join(outDir, 'index.json'), JSON.stringify({ source: `DynamicMaps @ ${commit}`, maps: index }, null, 2) + '\n')

// Marker icons (locked doors). game-icons.net originals (CC BY 3.0) that DynamicMaps scaled and outlined;
// copied unmodified together with DynamicMaps' credits file.
const markersDir = path.join(repo, 'Plugin/Resources/Markers')
const iconsDir = path.join(outDir, 'icons')
fs.mkdirSync(iconsDir, { recursive: true })
for (const file of ['door_with_lock.png', 'door_with_key.png', 'marker_credits.txt']) fs.copyFileSync(path.join(markersDir, file), path.join(iconsDir, file))
// Extract icons (10 spec): tarkov.dev public/maps/interactive/extract_*.png (MIT, drawn by Shebuka), kept unmodified in
// tools/maps/icons since DynamicMaps has no per-faction extract markers.
for (const file of fs.readdirSync(path.join(__dirname, 'icons'))) fs.copyFileSync(path.join(__dirname, 'icons', file), path.join(iconsDir, file))
console.log(`copied marker icons to ${iconsDir}`)
console.log(`wrote ${index.length} maps to ${outDir}`)
