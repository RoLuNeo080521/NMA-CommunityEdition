using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using NexusMods.Paths.Trees;
using NexusMods.Paths.Trees.Traits;
using NexusMods.Sdk.IO;
using NexusMods.Sdk.FileStore;
using NexusMods.Sdk.Library;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Loadouts;

namespace NexusMods.Games.RedEngine.ModInstallers;

public class RedModInstaller : ALibraryArchiveInstaller
{
    public RedModInstaller(IServiceProvider serviceProvider) : base(serviceProvider, serviceProvider.GetRequiredService<ILogger<RedModInstaller>>())
    {
        _fileStore = serviceProvider.GetRequiredService<IFileStore>();
    }
    
    private static readonly RelativePath InfoJson = "info.json";
    private static readonly RelativePath Mods = "mods";

    // Folders of the game for the other kinds of mods: an info.json there isn't a REDmod manifest
    private static readonly RelativePath[] GameFolders = ["bin", "engine", "r6", "red4ext", "archive"];

    // The script that Cyber Engine Tweaks runs for its mods
    private static readonly RelativePath CetModScript = "init.lua";
    private readonly IFileStore _fileStore;
    
    private async Task<RedModInfo?> ReadInfoJson(Hash hash, IStreamFactory? streamFactory = null)
    {
        try
        {
            await using var stream = await _fileStore.GetFileStream(hash);
            return await JsonSerializer.DeserializeAsync<RedModInfo>(stream);
        }
        catch (Exception ex)
        {
            // TODO: Remove this after we get rid of the old mod code
            if (streamFactory != null)
            {
                await using var streamDirect = await streamFactory.GetStreamAsync();
                return await JsonSerializer.DeserializeAsync<RedModInfo>(streamDirect);
            }
            Logger.LogError(ex, "Failed to read info.json for {Hash}", hash);
            return null;
        }
    }
    
    public override async ValueTask<InstallerResult> ExecuteAsync(
        LibraryArchive.ReadOnly libraryArchive,
        LoadoutItemGroup.New loadoutGroup,
        ITransaction tx,
        Loadout.ReadOnly loadout,
        CancellationToken cancellationToken)
    {
        var tree = LibraryArchiveTreeExtensions.GetTree(libraryArchive);
        var infosList = new List<(KeyedBox<RelativePath, LibraryArchiveTree>  File, RedModInfo InfoJson)>();
        var archivePaths = tree.GetFiles().Select(f => f.Item.Path).ToHashSet();
        
        foreach (var f in tree.GetFiles())
        {
            if (f.Key().FileName != InfoJson)
                continue;
            if (!IsRedModManifest(f.Item.Path, archivePaths))
                continue;

            var infoJson = await ReadInfoJson(f.Item.LibraryFile.Value.Hash);
            if (infoJson != null)
                infosList.Add((f, infoJson));
        }
        
        if (infosList.Count == 0) return new NotSupported(Reason: $"Archive contains no valid redmod files named `{InfoJson}`");

        foreach (var (file, infoJson) in infosList.OrderBy(x => x.InfoJson.Name))
        {
            var modFolder = file.Parent();
            var parentName = modFolder!.Segment();

            var foundManifest = false;
            
            foreach (var childNode in modFolder!.GetFiles().OrderBy(f => f.Item.Path))
            {
                var relativePath = childNode.Item.Path.RelativeTo(modFolder!.Item.Path);
                var joinedPath = Mods.Join(parentName).Join(relativePath);
                
                var newFile = childNode.ToLoadoutFile(loadout.Id, loadoutGroup.Id, tx, new GamePath(LocationId.Game, joinedPath));

                if (file.Item.Path == childNode.Item.Path)
                    foundManifest = true;
            }
            
            if (foundManifest == false)
                throw new InvalidOperationException("Failed to find the info.json file in the mod archive, this should never happen");
        }

        return new Success();
    }

    /// <summary>
    /// Other mods ship an info.json too, like the Cyber Engine Tweaks mods, in
    /// bin/x64/plugins/cyber_engine_tweaks/mods/&lt;mod&gt; next to their init.lua. A REDmod manifest isn't in the game
    /// folders of the other kinds of mods, nor next to an init.lua.
    /// </summary>
    internal static bool IsRedModManifest(RelativePath infoJsonPath, IReadOnlySet<RelativePath> archivePaths)
    {
        var modFolder = infoJsonPath.Parent;
        if (modFolder.Parts.Any(part => GameFolders.Contains(part))) return false;
        return !archivePaths.Contains(modFolder.Join(CetModScript));
    }
}

internal class RedModInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string? Description { get; set; } = string.Empty;
}
