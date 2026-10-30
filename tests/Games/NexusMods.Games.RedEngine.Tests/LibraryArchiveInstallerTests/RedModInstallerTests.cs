using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.RedEngine.Cyberpunk2077;
using NexusMods.Games.RedEngine.Cyberpunk2077.Models;
using NexusMods.Games.RedEngine.ModInstallers;
using NexusMods.Games.TestFramework;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Library;
using NexusMods.Sdk.Loadouts;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;

namespace NexusMods.Games.RedEngine.Tests.LibraryArchiveInstallerTests;

public class RedModInstallerTests(ITestOutputHelper outputHelper) : ALibraryArchiveInstallerTests<PathBasedInstallerTests, Cyberpunk2077Game>(outputHelper)
{
    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddRedEngineGames()
            .AddUniversalGameLocator<Cyberpunk2077Game>(new Version("1.6.1"));
    }
    
    [Theory]
    [InlineData("several_red_mods.7z")]
    [InlineData("one_mod.7z")]
    public async Task CanInstallRedMod(string filename)
    {
        var fullPath = FileSystem.GetKnownPath(KnownPath.EntryDirectory).Combine("LibraryArchiveInstallerTests/Resources/" + filename);

        var loadout = await CreateLoadout();
        var libraryArchive = await RegisterLocalArchive(fullPath);
        var group = await Install(typeof(RedModInstaller), loadout, libraryArchive);
        
        await VerifyGroup(libraryArchive, group).UseParameters(filename);
    }

    private const string InfoJson = """{ "name": "Bike V Control + Impact", "version": "16.0.4" }""";

    private const string CetMod = "bin/x64/plugins/cyber_engine_tweaks/mods/bike_mode_impact_topple";

    /// <summary>
    /// Creates a test archive with the given files and contents, and adds it to the library.
    /// </summary>
    private async Task<LibraryArchive.ReadOnly> AddFromFiles(params (string Path, string Content)[] files)
    {
        await using var file = TemporaryFileManager.CreateFile();
        {
            await using var archiveStream = file.Path.Create();
            using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Create);
            foreach (var (path, content) in files)
            {
                await using var stream = zip.CreateEntry(path).Open();
                await stream.WriteAsync(Encoding.UTF8.GetBytes(content));
            }
        }

        return await RegisterLocalArchive(file);
    }

    private async Task<InstallerResult> TryInstallRedMod(Loadout.ReadOnly loadout, LibraryArchive.ReadOnly archive)
    {
        var installer = Game.LibraryItemInstallers.OfType<RedModInstaller>().Single();
        using var tx = Connection.BeginTransaction();
        var libraryItem = archive.AsLibraryFile().AsLibraryItem();
        var group = new LoadoutItemGroup.New(tx, out var groupId)
        {
            IsGroup = true,
            LoadoutItem = new LoadoutItem.New(tx, groupId) { Name = libraryItem.Name, LoadoutId = loadout },
        };

        return await installer.ExecuteAsync(libraryItem, group, tx, loadout, CancellationToken.None);
    }

    private string[] InstalledPaths(LoadoutItemGroup.ReadOnly group) => ChildrenFilesAndHashes(group)
        .Select(file => file.GamePath)
        .Where(gamePath => gamePath.LocationId == LocationId.Game)
        .Select(gamePath => gamePath.Path.ToString())
        .Order()
        .ToArray();

    [Theory]
    [InlineData(CetMod)]
    [InlineData("bike_mode_impact_topple")]
    public async Task CyberEngineTweaksModWithInfoJson_IsNotARedMod(string modFolder)
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromFiles(($"{modFolder}/info.json", InfoJson), ($"{modFolder}/init.lua", "-- CET mod"));

        (await TryInstallRedMod(loadout, archive)).IsNotSupported(out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(CetMod)]
    [InlineData("bike_mode_impact_topple")]
    public async Task CyberEngineTweaksModWithInfoJson_TheGameInstallsItForCyberEngineTweaks(string modFolder)
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromFiles(($"{modFolder}/info.json", InfoJson), ($"{modFolder}/init.lua", "-- CET mod"));

        // With all the installers of the game, in their order
        var result = await LoadoutManager.InstallItem(archive.AsLibraryFile().AsLibraryItem(), loadout);

        InstalledPaths(result.LoadoutItemGroup!.Value).Should().Equal($"{CetMod}/info.json", $"{CetMod}/init.lua");
    }

    [Fact]
    public async Task CyberEngineTweaksModWithInfoJson_IsInstalledForCyberEngineTweaks()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromFiles(($"{CetMod}/info.json", InfoJson), ($"{CetMod}/init.lua", "-- CET mod"));

        var group = await Install(typeof(SimpleOverlayModInstaller), loadout, archive);

        InstalledPaths(group).Should().Equal($"{CetMod}/info.json", $"{CetMod}/init.lua");
    }

    [Fact]
    public async Task RedModInModsFolder_IsInstalledNextToCyberEngineTweaksMods()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromFiles(
            ("mods/my_redmod/info.json", """{ "name": "My REDmod", "version": "1.0" }"""),
            ("mods/my_redmod/archives/my_redmod.archive", "archive"),
            ($"{CetMod}/info.json", InfoJson),
            ($"{CetMod}/init.lua", "-- CET mod"));

        var group = await Install(typeof(RedModInstaller), loadout, archive);

        // The REDmod only: its info.json is the only REDmod manifest of the archive
        InstalledPaths(group).Should().Equal("mods/my_redmod/archives/my_redmod.archive", "mods/my_redmod/info.json");
    }
}
