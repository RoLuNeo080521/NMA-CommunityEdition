# CLAUDE.md

Instructions pour Claude Code (et tout contributeur) travaillant sur ce dépôt.

## Règle n°1

**Ne jamais supposer le contenu d'un fichier sans l'avoir lu.** Avant de citer un chemin, un nom de
fichier, une option de configuration ou une commande, vérifie qu'il existe réellement dans le dépôt
(`ls`, `find`, `grep`, lecture du fichier). N'invente aucun chemin.

## But du fork

**NMA Community Edition** est un fork communautaire et non officiel de
[Nexus-Mods/NexusMods.App](https://github.com/Nexus-Mods/NexusMods.App), dont le développement a été
mis en pause par Nexus Mods. Le fork est centré sur **Linux** (Steam/Proton, GOG via Heroic ou Lutris,
préfixes Wine) et distribué sous forme d'**AppImage**.

- AppId : `com.nmacommunity.app` (défini dans `src/NexusMods.App/app.pupnet.conf`)
- Nom affiché : `NMA Community Edition`, binaire `NMAcommunity.App`
- Projet de l'application : `src/NexusMods.App/NexusMods.App.csproj`
- Solution : `NexusMods.App.sln` (62 projets dans `src/`, 34 projets de test dans `tests/`, 1 dans `benchmarks/`)

## SDK .NET

- Les projets ciblent `net9.0` (`Directory.Build.props`).
- `global.json` demande le SDK `8.0.0` avec `rollForward: latestMajor` : un SDK .NET 9 est donc utilisé.
- Le packaging avec PupNet 1.9.1 nécessite en plus le **runtime .NET 8** (l'outil cible `net8.0`).

## Dépendances du build

Le build échoue si l'une de ces conditions n'est pas remplie :

1. **Sous-modules Git** (déclarés dans `.gitmodules`) :
   - `extern/SMAPI` : requis par `src/NexusMods.Games.StardewValley.SMAPI`, qui échoue avec
     `Missing SMAPI submodule!` s'il est absent.
   - `docs/Nexus` : thème MkDocs de la documentation.

   Le hook `SessionStart` de `.claude/settings.json` les initialise automatiquement en début de session.
   Sinon : `git submodule update --init --recursive`.
2. **Accès réseau à `data.nexusmods.com`** : `src/NexusMods.Networking.NexusWebApi` télécharge
   `https://data.nexusmods.com/file/nexus-data/games.json` vers `obj/games.json` au moment du build
   (via `curl` sous Linux), si le fichier n'existe pas déjà. Si le domaine est bloqué, le build échoue avec
   `error MSB3073 ... exited with code 56`. Dans un environnement cloud Claude Code, ce domaine doit
   figurer dans les domaines autorisés de l'environnement.

## Build

```bash
git submodule update --init --recursive
dotnet build --configuration Release
```

### Espace disque (VM cloud)

Le build complet de la solution (34 projets de test inclus) **remplit le disque de la VM cloud**
(environ 24 Go disponibles par session), car chaque projet de test recopie toutes ses dépendances dans son `bin/`.
La solution consiste à activer les options MSBuild de hard links. Dans l'environnement cloud, elles sont
**définies comme variables d'environnement**, que MSBuild lit comme des propriétés. Un simple
`dotnet build` en tient donc compte :

```
CreateHardLinksForCopyLocalIfPossible=true
CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true
CreateHardLinksForAdditionalFilesIfPossible=true
CreateHardLinksForCopyAdditionalFilesIfPossible=true
```

Si elles sont absentes (autre machine, nouvel environnement), passe-les explicitement :

```bash
dotnet build --configuration Release \
  -p:CreateHardLinksForCopyLocalIfPossible=true \
  -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true \
  -p:CreateHardLinksForAdditionalFilesIfPossible=true \
  -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true
```

En cas de « no space left on device », supprime les dossiers `bin/` et `obj/` (régénérables) puis
relance le build avec ces options.

## Packaging (AppImage)

La configuration PupNet se trouve dans `src/NexusMods.App/app.pupnet.conf` (et non à la racine). PupNet
1.9.x n'embarque plus `appimagetool` : `appimagetool-x86_64.AppImage` doit être présent dans le `PATH`.

```bash
# Outils (une fois) — ~/.local/bin doit être dans le PATH
dotnet tool install --global KuiperZone.PupNet --version 1.9.1
mkdir -p ~/.local/bin
curl -fsSL -o ~/.local/bin/appimagetool-x86_64.AppImage \
  https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage
chmod +x ~/.local/bin/appimagetool-x86_64.AppImage

# Génération (depuis le dossier du projet de l'app)
cd src/NexusMods.App
APPIMAGE_EXTRACT_AND_RUN=1 pupnet -y -k AppImage -p DefineConstants=INSTALLATION_METHOD_APPIMAGE
# -> src/NexusMods.App/Deploy/OUT/NMAcommunity.App.x86_64.AppImage
```

- La version vient de `AppVersionRelease` dans `app.pupnet.conf` ; `-v <version>` permet de la surcharger.
- `APPIMAGE_EXTRACT_AND_RUN=1` permet de lancer `appimagetool` sans FUSE (runners CI, conteneurs).
- Sans runtime .NET 8, `pupnet` refuse de démarrer (« You must install or update .NET »).
- PupNet ne fait qu'un `dotnet publish` du projet de l'application (self-contained, ReadyToRun,
  single-file), sans les projets de test.
- `src/NexusMods.App/Deploy/` n'est pas ignoré par `.gitignore` : ne pas le committer.

## CI (GitHub Actions)

- `.github/workflows/build-appimage.yml` : workflow principal du fork. Il se lance à chaque push (toutes
  branches) et manuellement, et publie l'artifact `nma-appimage`.
- Les workflows hérités de Nexus Mods qui faisaient doublon ou ne pouvaient pas fonctionner sur le fork
  (runner self-hosted, secrets de signature/NuGet absents, builds Windows) ont été restreints à un
  déclenchement manuel (`workflow_dispatch`). Voir le commentaire en tête de chaque fichier.
