// Dev tool: turns the   raw zone dumps into the bundled snapshot server/Data/quest-zones.json.
  // Keeps every vanilla zone (mod quests may reuse vanilla ids). Each point carries its collider boxes for area drawing.
  // Mod zones the dump picked up are dropped: the server reads them from each mod's WTT CustomQuestZones files at runtime.
  // Locked doors: only Door and KeycardDoor (same set DynamicMaps shows; containers, trunks and switches are dropped),
  // plus the Icebreaker's keypad, explosive and frozen hatch doors (11 spec §3; type Keypad / Explosive / Hatch).
  // Map variants (09 spec): dumps/variants/<variant>/<map>.json are scenes a map-replacing mod swaps in (ManimalInterchange).
  // They go under "variants" and the server swaps a whole map for them only when that mod is loaded.
  // Exits (10 spec): positions and English names come from exits/tarkovdev-exits.json (tools/maps/fetch-tarkovdev-exits.js),
  // not from the dumps. tarkov.dev draws the live (expanded) Interchange, so a variant gets the same exits as vanilla.
  // A dump from plugin 0.0.4+ carries Exits/Transits; such a map uses the dump positions instead (Lighthouse: the live game
  // moved two exits and removed the taxi V-Ex, so tarkov.dev is wrong or silent there). Names still come from tarkov.dev.
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
  // ManimalIcebreaker doors that open without a key (11 spec §3); the dump plugin 0.0.6 picks them up by Id or by
  // object name. Keypad codes: the fixed ones in the mod's icebreaker_passcodes.json (the rest are random per raid).
  const SZ1_CHARGE = '69a0174087a75d2cbd0842e8' // IcebreakerChainDoor.ChargeTpls[0]
  const GAS_TORCH = '9a449693dff5334122ed7388' // BlowtorchIds.Tpl
  const KEYLESS_DOORS = {
    door_Icebreaker_Indoor_01_00001: { type: 'Keypad' },
    door_Icebreaker_Indoor_01_00008: { type: 'Keypad' },
    door_Icebreaker_Indoor_01_00010: { type: 'Keypad' },
    door_Icebreaker_Indoor_01_00017: { type: 'Keypad' },
    door_Icebreaker_Indoor_01_00051: { type: 'Keypad', code: '312220' },
    door_Icebreaker_Indoor_01_00052: { type: 'Keypad', code: '312220' },
    door_Icebreaker_Indoor_01_00059: { type: 'Keypad', code: '312220' },
    door_Icebreaker_Indoor_01_00060: { type: 'Keypad', code: '312220' },
    door_Icebreaker_Indoor_02_00084: { type: 'Keypad' },
    door_Icebreaker_Outdoor_00000: { type: 'Explosive', key: SZ1_CHARGE },
  }
  const NAMED_DOORS = {
    INTERACTIVE_Icebreaker_exterior_hatchway_door_frozen: { type: 'Hatch', key: GAS_TORCH },
  }
  const exitsFile = path.join(__dirname, 'exits', 'tarkovdev-exits.json')
  const exitsByMap = fs.existsSync(exitsFile) ? JSON.parse(fs.readFileSync(exitsFile, 'utf8')).maps : {}
  // map -> exit key -> tarkov.dev English name, for dumped exits
  const tdNames = Object.fromEntries(Object.entries(exitsByMap).map(([m, e]) => [m, Object.fromEntries(e.exits.map((x) => [x.key, x.name]))]))
  
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
  const dumpedExits = {}
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
    const add = (key, type, pos, code) => {
      const d = { key, type, x: round(pos.X), y: round(pos.Y), z: round(pos.Z) }
      if (code) d.code = code
      if (!list.some((o) => o.key === d.key && o.x === d.x && o.y === d.y && o.z === d.z)) list.push(d)
    }
    for (const door of dump.Doors ?? []) {
      const keyless = KEYLESS_DOORS[door.Id]
      if (keyless) add(keyless.key ?? '', keyless.type, door.Position, keyless.code)
      else if (DOOR_TYPES.has(door.Type) && door.KeyId) add(door.KeyId, door.Type, door.Position)
    }
    for (const obj of dump.Named ?? []) {
      const named = NAMED_DOORS[obj.Name]
      if (named) add(named.key, named.type, obj.Position)
    }
    if (list.length === 0) delete doors[map]
    if (dump.Exits) dumpedExits[map] = exitsOfDump(dump)
  }
  return { zones, doors, dumpedExits }
  }
  
  // Live-removed exits have no tarkov.dev name and no SPT locale entry either.
  const EXIT_NAMES = { ' V-Ex_light': 'Road to Military Base V-Ex' }
  // Dumped exits whose tarkov.dev entry has another key. The Icebreaker helicopter trigger sits 1000 m under the ship
  // until the flare is shot, so its height comes from tarkov.dev too (x, z match the dump).
  const EXIT_ALIASES = { icebreaker: { Icebreaker_Exit_Heli: 'Heli' } }

  // tarkov.dev positions are the trigger collider's centre, so the dump uses the same point (falls back to the transform).
  function exitsOfDump(dump) {
    const centre = (row) => {
      const b = row.Bounds
      const p = b ? { X: (b.Min.X + b.Max.X) / 2, Y: (b.Min.Y + b.Max.Y) / 2, Z: (b.Min.Z + b.Max.Z) / 2 } : row.Position
      return { x: round(p.X), y: round(p.Y), z: round(p.Z) }
    }
    const map = dump.Location.toLowerCase()
    const named = tdNames[map] ?? {}
    const aliases = EXIT_ALIASES[map] ?? {}
    const exits = []
    for (const e of dump.Exits) {
      if (!e.Name || exits.some((x) => x.key === e.Name)) continue
      const alias = (exitsByMap[map]?.exits ?? []).find((x) => x.key === aliases[e.Name])
      const pos = centre(e)
      if (alias) exits.push({ key: e.Name, name: alias.name, ...pos, y: alias.y })
      else exits.push({ key: e.Name, name: named[e.Name] ?? EXIT_NAMES[e.Name] ?? e.Name.trim(), ...pos })
    }
    const transits = (dump.Transits ?? []).filter((t) => t.Id >= 0).map((t) => ({ id: String(t.Id), ...centre(t) }))
    exits.sort((a, b) => a.key.localeCompare(b.key))
    transits.sort((a, b) => Number(a.id) - Number(b.id))
    return { exits, transits }
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
function render({ zones, doors, exits }, pad) {
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
  lines.push(`${pad}  },`, `${pad}  "exits": {`)
  const exitMaps = Object.keys(exits).sort()
  exitMaps.forEach((map, i) => {
    lines.push(`${pad}    ${JSON.stringify(map)}: {`)
    for (const part of ['exits', 'transits']) {
      const list = exits[map][part]
      lines.push(`${pad}      ${JSON.stringify(part)}: [`)
      list.forEach((e, j) => lines.push(`${pad}        ${JSON.stringify(e)}${j < list.length - 1 ? ',' : ''}`))
      lines.push(`${pad}      ]${part === 'exits' ? ',' : ''}`)
    }
    lines.push(`${pad}    }${i < exitMaps.length - 1 ? ',' : ''}`)
  })
  lines.push(`${pad}  }`)
  return lines
}

const summary = (label, { zones, doors, exits }) => {
  const maps = Object.keys(zones)
  const total = maps.reduce((n, m) => n + Object.keys(zones[m]).length, 0)
  const doorTotal = Object.values(doors).reduce((n, list) => n + list.length, 0)
  const exitTotal = Object.values(exits).reduce((n, e) => n + e.exits.length, 0)
  const transitTotal = Object.values(exits).reduce((n, e) => n + e.transits.length, 0)
  return `${label}: ${maps.length} maps, ${total} zone ids, ${doorTotal} locked doors, ${exitTotal} exits, ${transitTotal} transits`
}

// tarkov.dev draws the expanded Interchange; exits that only exist in the expanded area stay out of the vanilla map
// (the variant keeps them). Path to River sits east of the vanilla map's edge.
const EXPANDED_ONLY = { interchange: ['shopping_sniper_exit'] }
const vanillaExits = Object.fromEntries(Object.entries(exitsByMap).map(([m, e]) =>
  [m, { ...e, exits: e.exits.filter((x) => !(EXPANDED_ONLY[m] ?? []).includes(x.key)) }]))
// A variant only swaps the maps it dumped, so it carries exits for those maps alone.
const exitsFor = (maps) => Object.fromEntries(Object.entries(exitsByMap).filter(([m]) => maps.includes(m)))

const dumped = collect(dumpDir)
const vanilla = { zones: dumped.zones, doors: dumped.doors, exits: { ...vanillaExits, ...dumped.dumpedExits } }
const variantsDir = path.join(dumpDir, 'variants')
const variants = (fs.existsSync(variantsDir) ? fs.readdirSync(variantsDir) : [])
  .filter((v) => fs.statSync(path.join(variantsDir, v)).isDirectory())
  .sort()
  .map((v) => {
    const data = collect(path.join(variantsDir, v))
    return [v, { zones: data.zones, doors: data.doors, exits: { ...exitsFor(Object.keys(data.zones)), ...data.dumpedExits } }]
  })

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
