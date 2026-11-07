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
- The in-game hotkey comes from the client plugin in the same zip. Skip the `BepInEx` folder if you don't want it

## Installation

1. Download `QuestCodex-<version>.zip` for the version you want from [Releases](../../releases).
2. Extract it and overwrite the resulting `SPT_Runtime` and `BepInEx` folders into your SPT install root (e.g. `C:\SPT`,
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

### Opening it in game

Press **`F4`** in game to open it in your default browser. You can change the key in the BepInEx config (F12).

Install [WebOverlay](https://sp-mod.com/mod/2879/weboverlay) 1.11.0 or newer as well and it opens in a window over
the game instead. This is optional, and exclusive fullscreen still uses the browser.

- If you get a certificate warning the first time you open it after starting the game, choose **Advanced → Continue**.
  It's the same warning you'd see in a browser.
- Close the window with the hotkey or the ✕ on its title bar.

## Features

The wiki, the quest guide and the progress pages (overview, raid prep, needed items) are described with screenshots on the docs site.

**[QuestCodex docs](https://viper-9.github.io/QuestCodex/)**

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
without the prior written permission of the copyright holder. See [LICENSE](LICENSE.md) for the full terms.

Third-party components remain under their own licenses — see [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES).
