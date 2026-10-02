// Dev tool: turns the   raw zone dumps into the bundled snapshot server/Data/quest-zones.json.
  // Keeps every vanilla zone (mod quests may reuse vanilla ids). Each point carries its collider boxes for area drawing.
  // Mod zones the dump picked up are dropped: the server reads them from each mod's WTT CustomQuestZones files at runtime.
  // Locked doors: only Door and KeycardDoor (same set DynamicMaps shows; containers, trunks and switches are dropped).
  // Map variants (09 spec): dumps/variants/<variant>/<map>.json are scenes a map-replacing mod swaps in (ManimalInterchange).
  // They go under "variants" and the server swaps a whole map for them only when that mod is loaded.
  // Usage: node build-snapshot.js [dumpDir] [outFile] [collectedWith] [modsDir]
  const fs = require('fs')
  const path = require('path')
  
  const root = path.resolve(__dirname, '../..')
  const dumpDir = process.argv[2] ?? path.join(__dirname, 'dumps')
  const outFile = process.argv[3] ?? path.join(root, 'server/Data/quest-zones.json')
  const collectedWith = process.argv[4] ?? 'EFT 0.16.9.40743'
  const modsDir = process.argv[5] ?? 'F:/SPT4.1.2/SPT_Runtime/user/mods'
  
  const round = (n) => Math.round(n * 100) / 100
  const DOOR_TYPES = new Set(['Door', 'KeycardDoor'])
  
  // WTT zones (map|id -> positions). A dumped zone is a mod copy only if a WTT zone has the same map, id and position
  // (within 1 m) — a mod that reuses a vanilla id at another spot must not knock the vanilla zone out.
  const wtt = new Map()
  for (const mod of fs.existsSync(modsDir) ? fs.readdirSync(modsDir) : []) {
    const dir = path.join(modsDir, mod, 'db', 'CustomQuestZones')
    if (!fs.existsSync(dir)) continue
    for (const file of fs.readdirSync(dir).filter((f) => f.endsWith('.json'))) {
      try {
        for (const z of JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'))) {
          const key = `${String(z.ZoneLocation).toLowerCase()}|${String(z.ZoneId).trim()}`
          if (!wtt.has(key)) wtt.set(key, [])
          wtt.get(key).push({ x: Number(z.Position.X), z: Number(z.Position.Z) })
        }
      } catch {}
    }
  }
  // Ground Zero high and low share the same zones; only one of them needs dumping (the server pairs the two maps).
  const isModCopy = (map, id, pos) =>
    [map, map.replace(/_high$/, '')].some((m) => (wtt.get(`${m}|${id}`) ?? []).some((w) => Math.hypot(w.x - pos.X, w.z - pos.Z) < 1))
  let modCopies = 0
  
  // Zones and doors of every <map>.json directly in dir (subfolders such as variants/ are not read here).
  function collect(dir) {
  const zones = {}
  const doors = {}
  for (const file of fs.readdirSync(dir).filter((f) => f.endsWith('.json')).sort()) {
    const dump = JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'))
    // The game reports some ids capitalized (Sandbox, RezervBase); the server's locations folder is lowercase.
    const map = dump.Location.toLowerCase()
    const byId = (zones[map] ??= {})
    for (const zone of dump.Zones) {
      const id = zone.Id?.trim()
      if (!id) continue
      if (isModCopy(map, id, zone.Position)) { modCopies++; continue }
      const point = { x: round(zone.Position.X), y: round(zone.Position.Y), z: round(zone.Position.Z) }
      const boxes = boxesOf(zone)
      if (boxes.length > 0) point.boxes = boxes
      const points = (byId[id] ??= [])
      if (!points.some((p) => p.x === point.x && p.y === point.y && p.z === point.z)) points.push(point)
    }
    const list = (doors[map] ??= [])
    for (const door of dump.Doors ?? []) {
      if (!DOOR_TYPES.has(door.Type) || !door.KeyId) continue
      const d = { key: door.KeyId, type: door.Type, x: round(door.Position.X), y: round(door.Position.Y), z: round(door.Position.Z) }
      if (!list.some((o) => o.key === d.key && o.x === d.x && o.y === d.y && o.z === d.z)) list.push(d)
    }
    if (list.length === 0) delete doors[map]
  }
  return { zones, doors }
  }
  
  /**
   * Area boxes (08 spec). Dumps from plugin 0.0.2+ carry each collider: a BoxCollider gives the real box (centre, size,
   * yaw) — the old Bounds is only its axis-aligned envelope and over-states rotated zones. y0/y1 is the collider's height
   * range, used to pick the floors an area spans. Older dumps fall back to that envelope with r = 0.
   */
  function boxesOf(zone) {
    const colliders = (zone.Colliders ?? []).filter((c) => c.Type === 'BoxCollider' && c.Center && c.Size)
    if (colliders.length > 0) {
      return colliders.map((c) => ({
        cx: round(c.Center.X), cz: round(c.Center.Z), sx: round(c.Size.X), sz: round(c.Size.Z),
        r: round(((c.Yaw % 360) + 360) % 360), y0: round(c.Min.Y), y1: round(c.Max.Y),
      }))
    }
    const b = zone.Bounds
    if (!b) return []
    return [{
      cx: round((b.Min.X + b.Max.X) / 2), cz: round((b.Min.Z + b.Max.Z) / 2),
      sx: round(b.Max.X - b.Min.X), sz: round(b.Max.Z - b.Min.Z), r: 0, y0: round(b.Min.Y), y1: round(b.Max.Y),
    }]
  }
  
  // Stable key order so regenerating produces a readable diff. pad indents a block nested under "variants".
function render({ zones, doors }, pad) {
  const lines = [`${pad}  "zones": {`]
  const maps = Object.keys(zones).sort()
  maps.forEach((map, i) => {
    lines.push(`${pad}    ${JSON.stringify(map)}: {`)
    const ids = Object.keys(zones[map]).sort()
    ids.forEach((id, j) => {
      const comma = j < ids.length - 1 ? ',' : ''
      lines.push(`${pad}      ${JSON.stringify(id)}: ${JSON.stringify(zones[map][id])}${comma}`)
    })
    lines.push(`${pad}    }${i < maps.length - 1 ? ',' : ''}`)
  })
  lines.push(`${pad}  },`, `${pad}  "doors": {`)
  const doorMaps = Object.keys(doors).sort()
  doorMaps.forEach((map, i) => {
    // Sorted by key, then position, so regenerating produces a stable diff.
    const list = doors[map].sort((a, b) => a.key.localeCompare(b.key) || a.x - b.x || a.z - b.z)
    lines.push(`${pad}    ${JSON.stringify(map)}: [`)
    list.forEach((d, j) => lines.push(`${pad}      ${JSON.stringify(d)}${j < list.length - 1 ? ',' : ''}`))
    lines.push(`${pad}    ]${i < doorMaps.length - 1 ? ',' : ''}`)
  })
  lines.push(`${pad}  }`)
  return lines
}

const summary = (label, { zones, doors }) => {
  const maps = Object.keys(zones)
  const total = maps.reduce((n, m) => n + Object.keys(zones[m]).length, 0)
  const doorTotal = Object.values(doors).reduce((n, list) => n + list.length, 0)
  return `${label}: ${maps.length} maps, ${total} zone ids, ${doorTotal} locked doors`
}

const vanilla = collect(dumpDir)
const variantsDir = path.join(dumpDir, 'variants')
const variants = (fs.existsSync(variantsDir) ? fs.readdirSync(variantsDir) : [])
  .filter((v) => fs.statSync(path.join(variantsDir, v)).isDirectory())
  .sort()
  .map((v) => [v, collect(path.join(variantsDir, v))])

const lines = ['{', `  "collectedWith": ${JSON.stringify(collectedWith)},`, ...render(vanilla, '')]
if (variants.length > 0) {
  lines[lines.length - 1] += ','
  lines.push('  "variants": {')
  variants.forEach(([v, data], i) => {
    lines.push(`    ${JSON.stringify(v)}: {`, ...render(data, '    '), `    }${i < variants.length - 1 ? ',' : ''}`)
  })
  lines.push('  }')
}
lines.push('}', '')

fs.mkdirSync(path.dirname(outFile), { recursive: true })
fs.writeFileSync(outFile, lines.join('\n'))
console.log(`wrote ${outFile} (${modCopies} mod zone copies dropped)`)
console.log(summary('vanilla', vanilla))
for (const [v, data] of variants) console.log(summary(`variant ${v}`, data))
