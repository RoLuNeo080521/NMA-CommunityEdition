using NexusMods.Games.CreationEngine.Abstractions;
using NexusMods.Sdk.Games;

namespace NexusMods.Games.CreationEngine.Fallout4;

public class Fallout4Synchronizer : ACreationEngineSynchronizer
{
    public Fallout4Synchronizer(IServiceProvider provider, ICreationEngineGame game) : base(provider, game)
    {

    }

    // Prefixes of vanilla base game + DLC files that ship in Data/. All the
    // .ba2 archives and .esm plugins from Bethesda follow these prefixes with
    // a suffix like " - Textures.ba2" or ".esm". Third-party mods use their
    // own names and are unaffected.
    private static readonly string[] VanillaDataPrefixes =
    [
        "Fallout4",
        "DLCRobot",           // Automatron
        "DLCworkshop01",      // Wasteland Workshop
        "DLCCoast",           // Far Harbor
        "DLCworkshop02",      // Contraptions Workshop
        "DLCworkshop03",      // Vault-Tec Workshop
        "DLCNukaWorld",       // Nuka-World
        "DLCUltraHighResolution",
    ];

    // Files in the game root that are shipped by Steam/GOG/Epic — the game
    // executable, its launcher and their runtime DLLs. Marking them ignored
    // stops NMA from treating a Steam re-verify as user-installed mods when
    // the upstream hashes DB doesn't cover this build.
    private static readonly HashSet<string> VanillaRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fallout4.exe",
        "Fallout4Launcher.exe",
        "CreationKit.exe",
        "steam_api64.dll",
        "msvcp140.dll",
        "vcruntime140.dll",
        "d3dcompiler_46.dll",
        "EOSSDK-Win64-Shipping.dll",
        "installscript.vdf",
    };

    public override bool IsIgnoredBackupPath(GamePath path)
    {
        if (path.LocationId != LocationId.Game)
            return false;

        var relative = path.Path.ToString();

        // Root vanilla files (case-insensitive to survive case oddities on Windows/Wine).
        if (!relative.Contains('/') && VanillaRootFiles.Contains(relative))
            return true;

        // Everything under Data/ that starts with a vanilla prefix. Covers
        // "Data/Fallout4.esm", "Data/Fallout4 - Meshes.ba2", "Data/DLCCoast.esm",
        // "Data/DLCNukaWorld - Voices_fr.ba2" etc.
        if (relative.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
        {
            var name = relative.AsSpan("Data/".Length);
            // Only match at the top of Data/, not deeper. Subfolders (e.g. mod
            // subfolders that happen to be named "Fallout4") stay manageable.
            if (name.Contains('/'))
                return false;

            foreach (var prefix in VanillaDataPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    // Ensure the match is bounded — the next char must be '.',
                    // ' ' (as in " - Textures.ba2") or end-of-string. Avoids
                    // matching e.g. "Fallout4Modded.esm".
                    if (name.Length == prefix.Length) return true;
                    var next = name[prefix.Length];
                    if (next == '.' || next == ' ') return true;
                }
            }
        }

        return false;
    }
}
