<div align="center">
  <h1>NMA Community Edition</h1>
  <p><strong>A community fork of the Nexus Mods App, built for modding on Linux.</strong></p>
  <p>
    <a href="https://github.com/RoLuNeo080521/NMA-CommunityEdition/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/RoLuNeo080521/NMA-CommunityEdition?sort=semver"></a>
    <a href="https://github.com/RoLuNeo080521/NMA-CommunityEdition/actions/workflows/tests.yml"><img alt="Tests" src="https://github.com/RoLuNeo080521/NMA-CommunityEdition/actions/workflows/tests.yml/badge.svg"></a>
    <a href="./LICENSE.md"><img alt="License: GPL-3.0" src="https://img.shields.io/badge/license-GPL--3.0-blue"></a>
  </p>
</div>

---

## Why this fork

Nexus Mods [paused the Nexus Mods App](https://help.nexusmods.com/article/164-discontinuing-the-nexus-mods-app-faq) to focus on Vortex. Vortex is a Windows app, so Linux players lost their native mod manager.

**NMA Community Edition** keeps the app going for them. It handles Steam and Proton, GOG games installed with Heroic or Lutris, and Wine prefixes, and it ships as a single AppImage.

> This is an **unofficial fork**. It is not affiliated with, endorsed by or supported by Nexus Mods. All credit for the original application goes to the upstream team and contributors.

## What the fork adds

- **GOG games installed with Lutris** are detected and launched, with recent Lutris versions and the Flatpak build.
- **Game versions newer than the Nexus file database** (for example Cyberpunk 2077 2.5) are handled safely. NMA takes the game folder as it first indexed it as the original game files, so "Clean folder" no longer deletes the game.
- **Fallout 4 end to end**, F4SE included, on Steam through Proton.
- **The Play button follows the running game** on Linux, through launcher-to-game hand-offs.
- **No telemetry.** No diagnostic or usage data is sent to Nexus Mods. The app identifies itself as NMA Community Edition to the Nexus Mods API.
- **A missing game no longer breaks the app.** If a managed game is uninstalled or moved, its loadouts are hidden until the game is found again.
- **Many fixes to mod files and downloads**: files shared by two mods, deleted files that came back, downloads stuck "in progress", leftover temporary archives.
- **Kept up to date**: ships .NET 10 and the latest runtime patch, keeps its dependencies free of known vulnerabilities, and tells you when a new release is out.

The [release notes](../../releases) list every change.

## Supported games

| Game | Notes |
|---|---|
| Cyberpunk 2077 | REDmod load order |
| Stardew Valley | SMAPI mods |
| Skyrim Special Edition | SKSE launch |
| Fallout 4 | F4SE launch |
| Baldur's Gate 3 | |
| Mount & Blade II: Bannerlord | |

On Linux, NMA finds games installed with:

- **Steam**, native and Proton games
- **Heroic Games Launcher** (GOG)
- **Lutris** (GOG), native and Flatpak
- **Wine prefixes** (GOG and Epic Games Store installs)

## Install

Download `NMAcommunity.App.x86_64.AppImage` from the [latest release](../../releases/latest), make it executable and run it:

```bash
chmod +x NMAcommunity.App.x86_64.AppImage
./NMAcommunity.App.x86_64.AppImage
```

- On a system without FUSE, run it with `APPIMAGE_EXTRACT_AND_RUN=1 ./NMAcommunity.App.x86_64.AppImage`.
- Data lives in `~/.local/share/NMAcommunity.App/`, and logs in `~/.local/state/NMAcommunity.App/`. Both are separate from the official app, so the two can be installed side by side.

## Roadmap

The fork follows what players need rather than a fixed plan. Likely next steps:

- **More games**, such as Starfield, Fallout: New Vegas and Oblivion, as time and testing allow.
- **More Linux launchers**: Epic games through Heroic, Lutris beyond GOG, Bottles.
- **Bug fixes first.** Reports on the [issues page](../../issues) set the priorities.

Suggestions and pull requests are welcome.

## Building from source

```bash
git clone --recurse-submodules https://github.com/RoLuNeo080521/NMA-CommunityEdition.git
cd NMA-CommunityEdition
dotnet build --configuration Release
```

You need:

- the .NET 10 SDK;
- network access to `data.nexusmods.com` during the build;
- the git submodules. In an existing clone, fetch them with `git submodule update --init --recursive`.

[`CLAUDE.md`](./CLAUDE.md) documents the build, tests, AppImage packaging and releases. It is written in French.

## Made with Claude

NMA Community Edition is maintained by [RoLuNeo080521](https://github.com/RoLuNeo080521) with **[Claude Code](https://claude.com/claude-code)**, Anthropic's AI coding assistant. Since v1.1.0, the fork's fixes and features have been built together:

- **Claude** tracks bugs down to their root cause, writes the fixes and their tests, and prepares the pull requests and release notes.
- **The maintainer** decides what goes in, tests each release's AppImage and publishes it.

That partnership is what lets a one-person fork ship fixes this quickly. Commits written with Claude carry a `Co-Authored-By: Claude` trailer, and [`CLAUDE.md`](./CLAUDE.md) holds the project rules Claude works by.

## Credits

All credit for the original application goes to the **Nexus Mods team** and the upstream contributors. The original repository is [Nexus-Mods/NexusMods.App](https://github.com/Nexus-Mods/NexusMods.App), and this fork builds on their work.

## License

GPL-3.0-only, as inherited from the upstream project. All original copyright notices are kept. The full text is in [`LICENSE.md`](./LICENSE.md).
