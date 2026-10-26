<div align="center">
  <h1>NMA Community Edition</h1>
  <p><em>A community-maintained fork of the Nexus Mods App, focused on keeping Linux modding alive.</em></p>
</div>

---

## Introduction

The official [Nexus Mods App](https://help.nexusmods.com/article/164-discontinuing-the-nexus-mods-app-faq) was paused by Nexus Mods / Black Tree Gaming Ltd. to refocus on Vortex. **NMA Community Edition** picks up where they left off, with a focus on the **Linux ecosystem** (Steam, GOG via Heroic or Lutris, Wine prefixes, etc.) — a use case the upstream app supported well but no longer maintains.

This is an **unofficial fork**, not affiliated with, endorsed by, or supported by Nexus Mods. All credit for the original application goes to the upstream team and contributors.

## Features

### Supported games

- **Cyberpunk 2077**
- **Stardew Valley**
- **Skyrim Special Edition**
- **Fallout 4**
- **Baldur's Gate 3**
- **Mount & Blade II: Bannerlord**

### Supported stores

- **Steam** (native + Proton)
- **GOG** (native, via Heroic Games Launcher or Lutris)
- **Epic Games Store** (where applicable per game)
- **Microsoft Store / Xbox Game Pass** (where applicable per game)

### Other

- **Linux-first**: bug fixes and packaging target Linux desktop environments first.
- **9 UI languages**: English, French, German, Italian, Polish, Portuguese (Brazil), Russian, Turkish, Ukrainian — switchable in Settings > General > Language.
- **AppImage** distribution for easy install on any Linux distribution.

## Roadmap

The fork follows community demand rather than a rigid roadmap. Likely directions:

- **More games**: porting / completing support for popular Nexus Mods titles (Starfield, Fallout: New Vegas, Oblivion, etc.) as time and testing allow.
- **More Linux launchers**: Bottles and other Wine front-ends, and Lutris beyond GOG games.
- **Continued bug fixes**: real-world issues reported on the [issues page](../../issues) drive priorities.

If you want to suggest something or contribute, open an issue or pull request.

## Installation

Grab the latest AppImage from the [Releases page](../../releases), make it executable, and run it:

```bash
chmod +x NMAcommunity.App.x86_64.AppImage
./NMAcommunity.App.x86_64.AppImage
```

Detailed changes for each version are in the release notes, not duplicated here.

## Building from source

```bash
git clone --recurse-submodules https://github.com/RoLuNeo080521/NMA-CommunityEdition.git
cd NMA-CommunityEdition
dotnet build
```

You'll need the .NET 9 SDK installed, and network access to `data.nexusmods.com` during the build. In an existing clone, fetch the submodules with `git submodule update --init --recursive`: the build fails without them.

## Credits

All credit for the original application goes to the **Nexus Mods team** and the upstream contributors. The original repository is [Nexus-Mods/NexusMods.App](https://github.com/Nexus-Mods/NexusMods.App). This fork merely maintains community patches on top of their work.

## License

GPL-3.0-only, as inherited from the upstream project. All original copyright notices are preserved. Full license text in [`LICENSE.md`](./LICENSE.md).
