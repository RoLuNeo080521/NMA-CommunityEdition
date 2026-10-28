# Contributing to NMA Community Edition

Thanks for helping! NMA Community Edition is a small, Linux-focused fork, and a few conventions keep it manageable.

## Reporting a problem

- Use the [issue forms](https://github.com/RoLuNeo080521/NMA-CommunityEdition/issues/new/choose):
  - **Report a bug** for a problem;
  - **Report a missing game** when the app doesn't find a supported game.
- Attach your logs. They are in `~/.local/state/NMAcommunity.App/Logs/`: attach the files starting with `nmacommunity.app.main.` and `nmacommunity.app.slim.`.
- Say which version of the app you use, your distribution, and how the game is installed: Steam, Proton, Heroic, Lutris or a Wine prefix.

## Suggesting a feature

Open a **Request a new feature** issue. The [roadmap](README.md#roadmap) lists the directions the fork already considers.

## Pull requests

- **Build and test** with the commands in [Building from source](README.md#building-from-source). [`CLAUDE.md`](CLAUDE.md), written in French, documents the build, the tests, the AppImage packaging and the releases.
- **Keep it focused:** one topic per pull request, based on `main`.
- **Test your fix:** a bug fix comes with a test that fails without it, when that is possible. Mark tests that make real HTTP calls with `[Trait("RequiresNetworking", "True")]`.
- **Commit messages** follow the history: a prefix such as `feat(games)`, `fix(sync)`, `ci`, `docs` or `chore`, then what the change does.
- **Keep CI green:** the AppImage build runs on every push, and the test suite on every push that touches the code.

## License

The project is GPL-3.0-only. By contributing, you agree that your contributions are licensed under the same terms.
