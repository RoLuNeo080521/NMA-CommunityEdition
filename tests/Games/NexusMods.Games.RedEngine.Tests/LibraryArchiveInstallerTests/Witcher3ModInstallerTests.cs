using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.RedEngine.ModInstallers;
using NexusMods.Games.RedEngine.Witcher3;
using NexusMods.Games.TestFramework;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;
using Xunit.DependencyInjection;

namespace NexusMods.Games.RedEngine.Tests.LibraryArchiveInstallerTests;

public class Witcher3ModInstallerTests(ITestOutputHelper outputHelper) : ALibraryArchiveInstallerTests<Witcher3ModInstallerTests, Witcher3Game>(outputHelper)
{
    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddRedEngineGames()
            .AddUniversalGameLocator<Witcher3Game>(new Version("4.04"));
    }

    public static IEnumerable<object[]> TestCaseData() =>
    [
        ["Mods Folder", new[] { "Mods/modFoo/content/blob0.bundle", "Mods/modFoo/content/scripts/game/player.ws" }],
        ["Lowercase Mods Folder In A Sub Folder", new[] { "My Mod/mods/modFoo/content/blob0.bundle" }],
        ["DLC Folder", new[] { "DLC/dlcFoo/content/blob0.bundle", "readme.txt" }],
        ["Mod Folder At Root", new[] { "modFoo/content/blob0.bundle", "modFoo/content/metadata.store" }],
        ["DLC Folder At Root", new[] { "dlcFoo/content/blob0.bundle" }],
        ["Mod Folder Without Mod Prefix", new[] { "Foo/content/blob0.bundle" }],
        ["Menu Mod", new[] { "Mods/modFoo/content/blob0.bundle", "bin/config/r4game/user_config_matrix/pc/foo.xml" }],
        ["Plugin", new[] { "bin/x64/plugins/foo.asi", "bin/x64_dx12/plugins/foo.asi" }],
    ];

    [Theory]
    [MethodData(nameof(TestCaseData))]
    public async Task FilesAreMappedToCorrectFolders(string testCaseName, string[] archivePaths)
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromPaths(archivePaths);
        var group = await Install(typeof(Witcher3ModInstaller), loadout, archive);

        await Verify(ChildrenFilesAndHashes(group).Select(row => new
        {
            FromPath = row.FromPath.ToString(),
            ToGamePath = row.GamePath.ToString(),
        })).UseParameters(testCaseName);
    }

    // A content folder alone is named after the archive, whose name is random in these tests
    [Fact]
    public async Task ContentFolderAtRootIsNamedAfterTheArchive()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromPaths(["content/blob0.bundle", "readme.txt"]);
        var group = await Install(typeof(Witcher3ModInstaller), loadout, archive);

        var gamePaths = ChildrenFilesAndHashes(group).Select(row => row.GamePath.Path.ToString()).ToArray();
        gamePaths.Should().ContainSingle().Which.Should().MatchRegex("^Mods/mod[A-Za-z0-9]+/content/blob0.bundle$");
    }

    [Theory]
    [InlineData("content/blob0.bundle", "Mods/modFoo/content/blob0.bundle")]
    [InlineData("modBar/content/blob0.bundle", "Mods/modBar/content/blob0.bundle")]
    [InlineData("readme.txt", null)]
    [InlineData("bin/readme.txt", null)]
    public void TargetPaths(string archivePath, string? expected)
    {
        Witcher3ModInstaller.GetTargetPath(archivePath.Split('/'), "modFoo").Should().Be(expected);
    }

    [Fact]
    public async Task ArchiveWithoutModFoldersIsNotSupported()
    {
        var loadout = await CreateLoadout();
        var archive = await AddFromPaths(["readme.txt", "images/screenshot.png"]);
        var installer = Game.LibraryItemInstallers.OfType<Witcher3ModInstaller>().Single();

        using var tx = Connection.BeginTransaction();
        var group = new LoadoutItemGroup.New(tx, out var groupId)
        {
            IsGroup = true,
            LoadoutItem = new LoadoutItem.New(tx, groupId) { Name = "test", LoadoutId = loadout },
        };
        var result = await installer.ExecuteAsync(archive, group, tx, loadout, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }
}
