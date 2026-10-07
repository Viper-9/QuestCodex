---
title: Known limitations
nav_order: 5
---

# Known limitations

## Mods that hook responses at runtime

QuestCodex reads the **quest tables the server merged into memory** once it finished booting. Every mod
that adds or edits quest JSON is reflected exactly, regardless of load order.

A mod that intercepts a route like `/client/quest/list` and **rewrites the response on each request** never
touches those tables, so its changes don't reach QuestCodex. You'd see the modified quest in game and the
unmodified one here.

{: .warning }
If the page and the game disagree, suspect this first.

## Mod quests added from C# code

Quests a mod injects from C# code (`CustomQuestService.CreateQuest()`) rather than from JSON files leave
no file trace, so their source mod can't be identified. Those get a generic `mod` label.

## Quest map coverage

- A few quests have no location data, so their objectives don't appear on the map.
- Mod quests show on the map only when the mod defines its zones in data the server can read, or when
  QuestCodex already ships their positions (the Icebreaker quests do).
- Maps a mod adds are drawn only when QuestCodex ships a map for them (currently the Icebreaker and the
  expanded Interchange).
- Extract and transit positions come from bundled data, so an extract a mod adds or moves may be missing
  or misplaced.
- QuestCodex doesn't track which keys you own. Lock icons tell you which key a door needs, nothing more.

## Unlock paths

The path summary checks the required level against your profile, but trader loyalty and standing are only
listed, not checked. **Quests left** counts quests only.

## Overridden vanilla quests

A vanilla quest is marked as overridden when a mod ships a file that replaces it. QuestCodex doesn't check whether
the mod actually changed anything in it.

## Kappa tracker

- Container chances for Collector items are worked out from your server's loot tables and averaged over the maps,
  so they're estimates, not drop rates you'll see in every raid.
- Only the Collector hand-in items get loot sources.

## Local only

The SPT server binds to `127.0.0.1`, so QuestCodex opens only in a browser on the machine running the server.
