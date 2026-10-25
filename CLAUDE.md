# CLAUDE.md

Instructions pour Claude Code (et tout contributeur) travaillant sur ce dépôt.

## Règles

- **Ne jamais supposer le contenu d'un fichier sans l'avoir lu.** Avant de citer un chemin, un nom de
  fichier, une option ou une commande, vérifie qu'il existe réellement dans le dépôt (`ls`, `find`,
  `grep`, lecture du fichier). N'invente aucun chemin.
- **Tenir ce fichier à jour.** Revérifie-le de temps en temps, et à chaque changement du build, du
  packaging ou de la CI : chaque information doit être encore exacte et utile pour développer sur ce
  dépôt. Supprime ce qui est obsolète, regroupe ce qui fait doublon, garde-le concis. Montre les
  changements à l'utilisateur avant de les committer.

## Le fork

**NMA Community Edition** est un fork communautaire non officiel de
[Nexus-Mods/NexusMods.App](https://github.com/Nexus-Mods/NexusMods.App). Il est centré sur **Linux**
(Steam/Proton, GOG via Heroic ou Lutris, préfixes Wine) et distribué en **AppImage**.

- AppId `com.nmacommunity.app`, nom affiché `NMA Community Edition`, binaire `NMAcommunity.App`.
- Application : `src/NexusMods.App/NexusMods.App.csproj`. Solution : `NexusMods.App.sln`.
- Les projets ciblent `net9.0` (`Directory.Build.props`). `global.json` demande le SDK `8.0.0` avec
  `rollForward: latestMajor`, c'est donc un SDK .NET 9 qui est utilisé.

## Build

Le build échoue sans :

- **le sous-module `extern/SMAPI`**. Sans lui, `src/NexusMods.Games.StardewValley.SMAPI` échoue avec
  `Missing SMAPI submodule!`. Le hook `SessionStart` de `.claude/settings.json` lance
  `git submodule update --init --recursive` à chaque session.
- **un accès réseau à `data.nexusmods.com`**. `src/NexusMods.Networking.NexusWebApi` y télécharge
  `games.json` (dans son `obj/`) s'il est absent. Si le domaine est bloqué, le build échoue avec
  `error MSB3073 ... exited with code 56`. En environnement cloud, il doit figurer dans les domaines
  autorisés.

Redirige toujours la sortie vers un fichier et n'affiche que les erreurs et le résumé, **jamais les
avertissements**, sauf demande explicite :

```bash
dotnet build --configuration Release > /tmp/build.log 2>&1
grep -E "error|Build succeeded|Build FAILED" /tmp/build.log | grep -v ': warning ' | tail -50
```

Le `grep -v ': warning '` est nécessaire, car certains avertissements contiennent le mot « error »
(CS8785, CS0618).

**Disque** : le build complet (projets de test inclus) remplit le disque de la VM cloud si les options
MSBuild de hard links ne sont pas activées. Dans l'environnement cloud, elles sont définies comme
variables d'environnement, que MSBuild lit comme des propriétés. Ailleurs, passe-les explicitement :

```bash
dotnet build --configuration Release \
  -p:CreateHardLinksForCopyLocalIfPossible=true \
  -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true \
  -p:CreateHardLinksForAdditionalFilesIfPossible=true \
  -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true > /tmp/build.log 2>&1
```

En cas de « no space left on device », supprime les dossiers `bin/` et `obj/` puis relance le build.

## Tests

Même règle que pour le build : sortie dans un fichier, n'afficher que les résumés et les tests en échec.

```bash
dotnet test --configuration Release --filter "RequiresNetworking!=True&FlakeyTest!=True" > /tmp/test.log 2>&1
grep -E "^(Passed|Failed|Skipped)! |^\s+Failed |^\s+(total|failed|succeeded):" /tmp/test.log
```

- La plupart des projets utilisent xunit (une ligne `Passed!`/`Failed!` par projet). `NexusMods.Backend.Tests`
  et `NexusMods.Sdk.Tests` utilisent TUnit (lignes `total:`/`failed:`/`succeeded:`).
- Les tests marqués `[Trait("RequiresNetworking", "True")]` ont besoin du réseau, et certains d'une clé
  `NEXUS_API_KEY`. Le filtre les exclut, en CI comme dans la VM cloud (qui bloque github.com). Marque ainsi
  tout nouveau test qui fait un appel HTTP réel.
- Tests de snapshot (Verify) : en cas d'écart, un fichier `*.received.*` (ignoré par git) apparaît à côté du
  `*.verified.*`. Compare-les ; si le changement est voulu, renomme le `.received` en `.verified`.

## Packaging (AppImage)

La configuration PupNet est dans `src/NexusMods.App/app.pupnet.conf`. Il faut :

- **PupNet 1.9.1**. C'est un outil `net8.0` : sans runtime .NET 8, il refuse de démarrer
  (« You must install or update .NET »).
- **`appimagetool-x86_64.AppImage` dans le `PATH`**, car PupNet ne l'embarque plus depuis la 1.9.0.

```bash
dotnet tool install --global KuiperZone.PupNet --version 1.9.1
mkdir -p ~/.local/bin   # ~/.local/bin doit être dans le PATH
curl -fsSL -o ~/.local/bin/appimagetool-x86_64.AppImage \
  https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage
chmod +x ~/.local/bin/appimagetool-x86_64.AppImage

cd src/NexusMods.App
APPIMAGE_EXTRACT_AND_RUN=1 pupnet -y -k AppImage -p DefineConstants=INSTALLATION_METHOD_APPIMAGE
# -> src/NexusMods.App/Deploy/OUT/NMAcommunity.App.x86_64.AppImage (Deploy/ est ignoré par git)
```

- `APPIMAGE_EXTRACT_AND_RUN=1` permet de lancer `appimagetool` sans FUSE (CI, conteneurs).
- La version vient de `AppVersionRelease` dans `app.pupnet.conf`. Elle peut être surchargée avec
  `-v <version>`.
- PupNet ne publie que le projet de l'application (`dotnet publish` self-contained, ReadyToRun,
  single-file), pas les projets de test.

## CI (GitHub Actions)

- `build-appimage.yml` se lance à chaque push (toutes branches), à la demande, et quand `release.yaml`
  l'appelle. Il publie l'artifact `nma-appimage`.
- `tests.yml` lance les tests (même filtre que ci-dessus) à chaque push qui touche au code.
- `release.yaml` publie une version (voir « Release » ci-dessous).
- `issue-maintenance.yaml`, `reformat-missing-game.yaml` (avec `.github/scripts/reformat-missing-game.js`),
  `stale.yaml` et `pr-maintenance.yaml` automatisent la gestion des issues et des PR.

## Release

1. Prépare une PR qui :
   - ajoute `.github/release-notes/vX.Y.Z.md`. Claude rédige ces notes à partir des commits depuis le
     dernier tag, au format des releases précédentes : sections « Highlights » (changements visibles),
     « Full changelog » (un commit par ligne) et « Install » ;
   - met `AppVersionRelease` à `X.Y.Z` dans `src/NexusMods.App/app.pupnet.conf`.
2. Une fois la PR mergée, lance **Actions → Release** avec la version `vX.Y.Z`. Le `v` doit être en
   minuscule, car le vérificateur de mises à jour de l'app ne retire qu'un `v` minuscule avant de lire la
   version. Le workflow refuse un tag existant ou des notes absentes. Il compile l'AppImage du commit visé
   avec cette version, puis crée la release avec l'AppImage seule (en brouillon par défaut).
3. Teste l'AppImage du brouillon, puis publie la release.
