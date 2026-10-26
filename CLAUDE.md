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
- Les projets ciblent `net10.0` (`Directory.Build.props`). `global.json` demande un SDK ≥ 10.0.100
  (`rollForward: latestMajor`) et fait tourner `dotnet test` sur Microsoft.Testing.Platform (voir « Tests »).

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
dotnet test --configuration Release --treenode-filter "/**[(RequiresNetworking!=True)&(FlakeyTest!=True)]" \
  --ignore-exit-code 8 > /tmp/test.log 2>&1
grep -E "^failed |\) failed|^Handshake failures|^\s+(total|failed|succeeded|skipped|error):" /tmp/test.log
```

- Les tests tournent sur Microsoft.Testing.Platform : chaque projet `*.Tests` est un exécutable. Les projets
  xunit 2 passent par `YTest.MTP.XUnit2`, ajouté par `tests/Directory.Build.props`. `NexusMods.Backend.Tests`
  et `NexusMods.Sdk.Tests` utilisent TUnit, qui a son propre runner, et retirent ce paquet.
- `--treenode-filter` marche avec xunit et TUnit (`--filter` seulement avec xunit). Il exclut les tests
  marqués `[Trait("RequiresNetworking", "True")]` (réseau, parfois une clé `NEXUS_API_KEY`) ou
  `[Trait("FlakeyTest", "True")]` (en TUnit : `[Property(...)]`). Marque ainsi tout nouveau test qui fait
  un appel HTTP réel. `--ignore-exit-code 8` évite l'échec d'un projet dont le filtre exclut tous les tests.
- `tests/Directory.Build.targets` lance les tests depuis leur dossier de sortie, comme VSTest : des tests
  lisent leurs ressources par un chemin relatif.
- Une bibliothèque d'aide sous `tests/` qui référence xunit doit déclarer `<IsTestProject>false</IsTestProject>`,
  sinon `dotnet test` la prend pour un projet VSTest et refuse de démarrer.
- Snapshots (Verify) : en cas d'écart, un `*.received.*` (ignoré par git) apparaît à côté du
  `*.verified.*`. Si le changement est voulu, renomme le `.received` en `.verified`.

## Packaging (AppImage)

La configuration PupNet est dans `src/NexusMods.App/app.pupnet.conf`. Il faut :

- **PupNet 1.10.0**, un outil `net10.0`.
- **`appimagetool-x86_64.AppImage` dans le `PATH`**, car PupNet ne l'embarque plus depuis la 1.9.0.

```bash
dotnet tool install --global KuiperZone.PupNet --version 1.10.0
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
- `tests.yml` : à chaque push qui touche au code, avec la commande ci-dessus (plus `--timeout 20m`).
- Les deux utilisent le SDK .NET 10 de l'image `ubuntu-latest`, sans `actions/setup-dotnet` (le CDN de
  Microsoft lui a renvoyé des erreurs 409). Si l'image n'en a plus, une étape de vérification échoue avec
  un message explicite.

## Release

Numérotation `MAJEUR.MINEUR.CORRECTIF` : un correctif (1.2.1) pour des corrections de bugs ou une mise à
jour de .NET ou des dépendances ; une mineure (1.3.0) pour une nouveauté visible (jeu, lanceur, fonction) ;
une majeure (2.0.0) pour un changement qui casse l'existant (données plus lisibles, plateforme abandonnée).

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
