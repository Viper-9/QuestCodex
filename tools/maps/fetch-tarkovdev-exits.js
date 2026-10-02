// Dev tool: pulls extract and transit positions from tarkov.dev's static JSON (10 spec) into a committed file that
// build-snapshot.js folds into server/Data/quest-zones.json.
//   json.tarkov.dev/regular/maps     -> extracts[].name (the game key, same as the server's allExtracts Name),
//                                       transits[].id (same as the server's base.transits id), positions
//   json.tarkov.dev/regular/maps_en  -> English extract names (every UI language shows these)
// The server decides which exits exist (its own DB); this file only supplies positions and names, so live-only exits
// here are simply never drawn.
// Usage: node fetch-tarkovdev-exits.js [outFile]
const fs = require('fs')
const path = require('path')

const outFile = process.argv[2] ?? path.resolve(__dirname, '../zone-dump/exits/tarkovdev-exits.json')
const BASE = 'https://json.tarkov.dev/regular'

const round = (n) => Math.round(n * 100) / 100
const point = (p) => ({ x: round(p.x), y: round(p.y), z: round(p.z) })

async function get(name) {
  const res = await fetch(`${BASE}/${name}`)
  if (!res.ok) throw new Error(`${name}: HTTP ${res.status}`)
  return (await res.json()).data
}

async function main() {
  const [{ maps }, names] = await Promise.all([get('maps'), get('maps_en')])
  const out = {}
  for (const m of Object.values(maps).sort((a, b) => a.nameId.localeCompare(b.nameId))) {
    // tarkov.dev capitalizes some ids (Interchange, RezervBase); our map keys are the lowercase locations folder names.
    const key = m.nameId.toLowerCase()
    const seen = new Set()
    const exits = []
    for (const e of m.extracts ?? []) {
      // The same key appears once per faction (pmc + scav rows of one exit); one position is enough.
      if (seen.has(e.name) || !e.position) continue
      seen.add(e.name)
      exits.push({ key: e.name, name: names[e.name] ?? e.name, ...point(e.position) })
    }
    const transits = (m.transits ?? []).filter((t) => t.position).map((t) => ({ id: String(t.id), ...point(t.position) }))
    exits.sort((a, b) => a.key.localeCompare(b.key))
    transits.sort((a, b) => Number(a.id) - Number(b.id))
    if (exits.length > 0 || transits.length > 0) out[key] = { exits, transits }
  }

  const lines = ['{', `  "source": "${BASE}/maps",`, `  "fetchedAt": "${new Date().toLocaleDateString('sv')}",`, '  "maps": {']
  const keys = Object.keys(out)
  keys.forEach((key, i) => {
    const { exits, transits } = out[key]
    lines.push(`    ${JSON.stringify(key)}: {`, '      "exits": [')
    exits.forEach((e, j) => lines.push(`        ${JSON.stringify(e)}${j < exits.length - 1 ? ',' : ''}`))
    lines.push('      ],', '      "transits": [')
    transits.forEach((t, j) => lines.push(`        ${JSON.stringify(t)}${j < transits.length - 1 ? ',' : ''}`))
    lines.push('      ]', `    }${i < keys.length - 1 ? ',' : ''}`)
  })
  lines.push('  }', '}', '')

  fs.mkdirSync(path.dirname(outFile), { recursive: true })
  fs.writeFileSync(outFile, lines.join('\n'))
  console.log(`wrote ${outFile}`)
  for (const key of keys) console.log(`  ${key}: ${out[key].exits.length} exits, ${out[key].transits.length} transits`)
}

main().catch((e) => { console.error(e.message); process.exit(1) })
