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
[Nexus-Mods/NexusMods.App](https://github.com/Nexus-Mods/NexusMods.App), centré sur **Linux**
(Steam/Proton, GOG via Heroic ou Lutris, préfixes Wine) et distribué en **AppImage**.

- AppId `com.nmacommunity.app`, nom affiché `NMA Community Edition`, binaire `NMAcommunity.App`.
- Application : `src/NexusMods.App/NexusMods.App.csproj`. Solution : `NexusMods.App.sln`.
- Les projets ciblent `net9.0` (`Directory.Build.props`). `global.json` (`8.0.0`, `rollForward: latestMajor`)
  prend le SDK le plus récent installé : il en faut un ≥ 9, et un SDK 9 pour les tests (voir « Tests »).

## Build

Le build échoue sans :

- **le sous-module `extern/SMAPI`** (`Missing SMAPI submodule!`). Le hook `SessionStart` de
  `.claude/settings.json` lance `git submodule update --init --recursive` à chaque session.
- **un accès réseau à `data.nexusmods.com`**, où `src/NexusMods.Networking.NexusWebApi` télécharge
  `games.json` (dans son `obj/`) s'il est absent. Sinon, `curl` échoue avec `error MSB3073`. En
  environnement cloud, ce domaine doit être autorisé.

Redirige toujours la sortie vers un fichier et n'affiche que les erreurs et le résumé, **jamais les
avertissements**, sauf demande explicite :

```bash
dotnet build --configuration Release > /tmp/build.log 2>&1
grep -E "error|Build succeeded|Build FAILED" /tmp/build.log | grep -v ': warning ' | tail -50
```

Le `grep -v ': warning '` est nécessaire, car certains avertissements contiennent le mot « error »
(CS8785, CS0618).

**Disque** : sans les options MSBuild de hard links, le build complet (tests inclus) remplit le disque de
la VM cloud. Elles y sont définies en variables d'environnement, comme dans les workflows. Ailleurs,
passe-les explicitement :

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

- Lance les tests avec un SDK 9 : à partir du SDK 10, `dotnet test` refuse les projets TUnit (« Testing
  with VSTest target is no longer supported… »).
- Les projets utilisent xunit (une ligne `Passed!`/`Failed!` par projet), sauf `NexusMods.Backend.Tests`
  et `NexusMods.Sdk.Tests`, en TUnit (lignes `total:`/`failed:`/`succeeded:`).
- Le filtre exclut les tests marqués `[Trait("RequiresNetworking", "True")]` (réseau, parfois une clé
  `NEXUS_API_KEY`) ou `[Trait("FlakeyTest", "True")]`. Marque ainsi tout nouveau test qui fait un appel
  HTTP réel.
- Snapshots (Verify) : en cas d'écart, un `*.received.*` (ignoré par git) apparaît à côté du
  `*.verified.*`. Si le changement est voulu, renomme le `.received` en `.verified`.

## Packaging (AppImage)

La configuration PupNet est dans `src/NexusMods.App/app.pupnet.conf`. Il faut :

- **PupNet 1.9.1**, un outil `net8.0` : sans runtime .NET 8, il refuse de démarrer (« You must install
  or update .NET »).
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

- `APPIMAGE_EXTRACT_AND_RUN=1` lance `appimagetool` sans FUSE (CI, conteneurs).
- La version vient de `AppVersionRelease` dans `app.pupnet.conf`, ou de `-v <version>`. Elle devient aussi
  la version de l'app, que le vérificateur de mises à jour compare aux tags des releases.

## CI (GitHub Actions)

- `build-appimage.yml` : à chaque push (toutes branches), à la demande, et appelé par `release.yaml`.
  Publie l'artifact `nma-appimage`.
- `tests.yml` : à chaque push qui touche au code, avec le filtre ci-dessus. Il épingle le SDK 9 le plus
  récent du runner via un `global.json` généré.
- Les deux utilisent les SDK .NET de l'image `ubuntu-latest`, sans `actions/setup-dotnet` (le CDN de
  Microsoft lui a renvoyé des erreurs 409). Si l'image n'a plus .NET 8 ou 9, une étape de vérification
  échoue avec un message explicite.

## Release

1. Une PR :
   - ajoute `.github/release-notes/vX.Y.Z.md`, que Claude rédige à partir des commits depuis le dernier
     tag, au format de `v1.2.0.md` : « Highlights » (changements visibles), « Full changelog » (un commit
     par ligne), « Install » ;
   - met `AppVersionRelease` à `X.Y.Z[1]` dans `src/NexusMods.App/app.pupnet.conf`.
2. Une fois la PR mergée, lance **Actions → Release** avec `vX.Y.Z`. Le `v` doit être minuscule : les
   versions 1.1.0 et antérieures de l'app ne reconnaissent pas un `V` majuscule. Le workflow refuse un tag
   existant ou des notes absentes, compile l'AppImage du commit visé avec cette version et crée la release
   avec l'AppImage seule (en brouillon par défaut).
3. Teste l'AppImage du brouillon, puis publie la release.
