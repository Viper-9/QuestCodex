#!/usr/bin/env node
// Keys each quest objective needs, from tarkov.dev, written to server/Data/required-keys.json.
//
//   node tools/gen-required-keys.mjs [regular|pve]
//
// Reads the JSON API the tarkov.dev site runs on (json.tarkov.dev), not the GraphQL API.
// objectives[].requiredKeys is a list of groups: every group is needed, any one key of a group is enough.
import { writeFile } from "node:fs/promises";

const mode = process.argv[2] ?? "regular";
const url = `https://json.tarkov.dev/${mode}/tasks`;
const res = await fetch(url);
if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
const { data } = await res.json();

const objectives = {};
for (const task of Object.values(data.tasks).sort((a, b) => a.id.localeCompare(b.id))) {
  for (const o of [...(task.objectives ?? [])].sort((a, b) => a.id.localeCompare(b.id))) {
    const groups = (o.requiredKeys ?? []).map((g) => (g ?? []).filter(Boolean).sort()).filter((g) => g.length > 0);
    if (groups.length > 0) objectives[o.id] = groups;
  }
}

const out = new URL("../server/Data/required-keys.json", import.meta.url);
const body = { source: url, fetchedAt: new Date().toISOString().slice(0, 10), objectives };
await writeFile(out, JSON.stringify(body, null, 2).replace(/\[\s+("[0-9a-f]{24}"(?:,\s+"[0-9a-f]{24}")*)\s+\]/g, (_, ids) => `[${ids.replace(/,\s+/g, ", ")}]`) + "\n");
console.log(`wrote ${Object.keys(objectives).length} objectives to server/Data/required-keys.json`);
