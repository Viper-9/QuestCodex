# QuestCodex

**Language: [한국어](README.md) | [English](README.en.md)**

## Purpose

A browser-based quest wiki for **the quests actually loaded on your own SPT server**.

External wiki sites track live EFT, which is not what your server runs. QuestCodex does the opposite: it
reads the quest tables the server holds in memory and builds the catalog from those. So if a quest mod
changed a condition or a reward, you see the changed value, and quests a mod added show up in the same
list as the vanilla ones — tagged with **which mod they came from**.

It only **reads** server data. It never modifies quests, profiles or traders, so alongside other quest
mods it simply shows you what those mods did.

## Requirements

- SPT server `~4.1.6`
- **No client plugin** — this is a server mod only, no BepInEx involved

## Installation

1. Download `QuestCodex-<version>.zip` for the version you want from [Releases](../../releases).
2. Extract it and overwrite the resulting `SPT_Runtime` folder into your SPT install root (e.g. `C:\SPT`,
   the parent folder of the `SPT_Runtime` folder containing `SPT.Server.exe`).
   (`SPT_Runtime/user/mods/QuestCodex/...` structure)
   - If your server root is the `SPT_Runtime` folder itself (older SPT layout), you can instead copy just
     the `SPT_Runtime\user\mods\QuestCodex` folder from the zip into your server root's `user\mods\`.
3. Restart the server.

## Opening it

Start the server, then open **`https://127.0.0.1:6969/questcodex`** in your browser.
You can also reach it from the mod card on the SPT server web page (`https://127.0.0.1:6969`).

- If you get a certificate warning, proceed — it's the same self-signed certificate the SPT server web
  page uses.
- The SPT server binds to `127.0.0.1` only, so the page opens **only in a browser on the machine running
  the server**. Phones and other PCs cannot reach it.
- You can keep it open on a second monitor while playing. It's read-only and doesn't touch the game.

## Features

**Wiki page**

- **Trader filter** — pick a trader from the avatar row to see only their quests. Mod-added traders are listed too.
- **Search and filter chips** — search by quest name, plus `Vanilla` / `Mod` and `BEAR only` / `USEC only`.
- **Sorting** — `By chain` (default, prerequisites always above) / `By level` / `By name`.
- **Inline expansion** — click a quest row and it expands in place with objectives, requirements, rewards
  (including on-accept and on-fail rewards) and its prerequisite/unlock links. Several rows can stay open at once.
- **Chain jumps** — click a prerequisite or unlocked quest name in the expanded row to jump to it.
- **Description popup** — the full quest description opens in a popup, so long text doesn't stretch the list.
- **Deep links** — `?quest=<questId>` opens a specific quest directly.
- **Mod attribution** — quests from mods carry their source mod's name, color-coded per mod.
- **Language switch** — `en` / `kr`. UI strings and quest/item names change together
  (game text comes from the server's locale tables).
- **Themes** — `System` / `Light` / `Dark`, remembered in the browser.

## Coming next

- **Progress page** — the left menu has the entry, but it's a "coming soon" line for now. It will show
  per-profile quest progress, including a Kappa tracker.
- Reverse reward lookup

## Known limitations

**Mods that hook responses at runtime are invisible here.**

QuestCodex reads the **quest tables the server merged into memory** once it finished booting. That means
every mod that adds or edits quest JSON is reflected exactly, regardless of load order. But a mod that
intercepts a route like `/client/quest/list` and **rewrites the response on each request** never touches
those tables, so its changes don't reach QuestCodex. You'd see the modified quest in game and the
unmodified one here.

It isn't a common approach, but it is a blind spot the design cannot avoid — so if the page and the game
disagree, suspect this first.

Also:

- Quests a mod injects **from C# code** (`CustomQuestService.CreateQuest()`) rather than from JSON files
  leave no file trace, so their source mod can't be identified. Those get a generic `mod` label instead of
  a mod name.

## License

Copyright (c) 2026 Viper-9. **All Rights Reserved.**

You may download and use this mod, unmodified, for personal, non-commercial purposes. Redistribution
(including re-uploading elsewhere), modification, derivative works and commercial use are prohibited
without the prior written permission of the copyright holder. See [LICENSE](LICENSE) for the full terms.

Third-party components remain under their own licenses — see [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES).
