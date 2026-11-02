using System.Collections.Immutable;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Diagnostics.Emitters;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Games.FileHashes.Emitters;
using NexusMods.Games.FOMOD;
using NexusMods.Games.RedEngine.ModInstallers;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.IO;

namespace NexusMods.Games.RedEngine.Witcher3;

[UsedImplicitly]
public class Witcher3Game : IGame, IGameData<Witcher3Game>
{
    public static GameId GameId { get; } = GameId.From("RedEngine.Witcher3");
    public static string DisplayName => "The Witcher 3: Wild Hunt";
    public static Optional<Sdk.NexusModsApi.NexusModsGameId> NexusModsGameId => Sdk.NexusModsApi.NexusModsGameId.From(952);

    public StoreIdentifiers StoreIdentifiers { get; } = new(GameId)
    {
        // The base game, and the Game of the Year edition
        SteamAppIds = [292030u, 499450u],
        GOGProductIds = [1207664643L, 1207664663L, 1495134320L, 1640424747L],
        EGSCatalogItemId = ["725a22e15ed74735bb0d6a19f3cc82d0"],
    };

    public IStreamFactory IconImage { get; } = new EmbeddedResourceStreamFactory<Witcher3Game>("NexusMods.Games.RedEngine.Resources.Witcher3.thumbnail.webp");
    public IStreamFactory TileImage { get; } = new EmbeddedResourceStreamFactory<Witcher3Game>("NexusMods.Games.RedEngine.Resources.Witcher3.tile.webp");

    private readonly Lazy<ILoadoutSynchronizer> _synchronizer;
    public ILoadoutSynchronizer Synchronizer => _synchronizer.Value;
    public ILibraryItemInstaller[] LibraryItemInstallers { get; }
    private readonly Lazy<ISortOrderManager> _sortOrderManager;
    public ISortOrderManager SortOrderManager => _sortOrderManager.Value;
    public IDiagnosticEmitter[] DiagnosticEmitters { get; }

    public Witcher3Game(IServiceProvider provider)
    {
        _synchronizer = new Lazy<ILoadoutSynchronizer>(() => new DefaultSynchronizer(provider));
        _sortOrderManager = new Lazy<ISortOrderManager>(() =>
        {
            var sortOrderManager = provider.GetRequiredService<SortOrderManager>();
            sortOrderManager.RegisterSortOrderVarieties([], this);
            return sortOrderManager;
        });

        DiagnosticEmitters =
        [
            new NoWayToSourceFilesOnDisk(),
            new UndeployableLoadoutDueToMissingGameFiles(provider),
        ];

        LibraryItemInstallers =
        [
            FomodXmlInstaller.Create(provider, new GamePath(LocationId.Game, "")),
            new Witcher3ModInstaller(provider),
        ];
    }

    public ImmutableDictionary<LocationId, AbsolutePath> GetLocations(IFileSystem fileSystem, GameLocatorResult gameLocatorResult)
    {
        // Documents/The Witcher 3 isn't managed for now: it holds the saves, which removing a loadout could delete
        return new Dictionary<LocationId, AbsolutePath>()
        {
            { LocationId.Game, gameLocatorResult.Path },
        }.ToImmutableDictionary();
    }

    // The remastered game only ships the DirectX 12 build, the versions before it only the DirectX 11 one
    private static readonly GamePath Dx12Executable = new(LocationId.Game, "bin/x64_dx12/witcher3.exe");
    private static readonly GamePath Dx11Executable = new(LocationId.Game, "bin/x64/witcher3.exe");

    public GamePath GetPrimaryFile(GameInstallation installation) =>
        installation.Locations.ToAbsolutePath(Dx12Executable).FileExists ? Dx12Executable : Dx11Executable;
}
