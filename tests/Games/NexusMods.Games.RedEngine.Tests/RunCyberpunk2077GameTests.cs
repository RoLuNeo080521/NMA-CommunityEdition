using FluentAssertions;
using NexusMods.Games.RedEngine.Cyberpunk2077;
using NexusMods.Paths;

namespace NexusMods.Games.RedEngine.Tests;

/// <summary>
/// When the game is launched with -modded: only when a REDmod is installed.
/// </summary>
public class RunCyberpunk2077GameTests : IDisposable
{
    private readonly AbsolutePath _gameFolder = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"nma-cyberpunk-launch-{Guid.NewGuid():N}");

    private AbsolutePath ModsFolder => _gameFolder.Combine("mods");

    public void Dispose()
    {
        if (_gameFolder.DirectoryExists()) _gameFolder.DeleteDirectory(recursive: true);
    }

    [Fact]
    public void NoModsFolder_HasNoRedMods()
    {
        RunCyberpunk2077Game.HasRedMods(ModsFolder).Should().BeFalse();
    }

    [Fact]
    public async Task ModsFolderWithOnlyAStubFile_HasNoRedMods()
    {
        // What is left in the folder once the last REDmod is removed
        ModsFolder.CreateDirectory();
        await ModsFolder.Combine(".stub").WriteAllTextAsync("");

        RunCyberpunk2077Game.HasRedMods(ModsFolder).Should().BeFalse();
    }

    [Fact]
    public void FolderWithoutInfoJson_HasNoRedMods()
    {
        // REDmod skips it: "Folder X is not a valid mod: Missing info.json!"
        ModsFolder.Combine("NotARedMod").Combine("archives").CreateDirectory();

        RunCyberpunk2077Game.HasRedMods(ModsFolder).Should().BeFalse();
    }

    [Fact]
    public async Task FolderWithInfoJson_HasRedMods()
    {
        var redMod = ModsFolder.Combine("MyRedMod");
        redMod.CreateDirectory();
        await redMod.Combine("info.json").WriteAllTextAsync("""{ "name": "MyRedMod", "version": "1.0.0" }""");

        RunCyberpunk2077Game.HasRedMods(ModsFolder).Should().BeTrue();
    }

    [Fact]
    public void WithoutRedMods_NoFlagIsAdded()
    {
        RunCyberpunk2077Game.BuildCommandLineArgs(null, hasRedMods: false).Should().BeNull();
        RunCyberpunk2077Game.BuildCommandLineArgs(["-skipStartScreen"], hasRedMods: false).Should().Equal("-skipStartScreen");
    }

    [Fact]
    public void WithRedMods_TheFlagIsAdded()
    {
        RunCyberpunk2077Game.BuildCommandLineArgs(null, hasRedMods: true).Should().Equal("-modded");
        RunCyberpunk2077Game.BuildCommandLineArgs(["-skipStartScreen"], hasRedMods: true).Should().Equal("-skipStartScreen", "-modded");
    }

    [Fact]
    public void WithRedMods_TheFlagIsNotAddedTwice()
    {
        RunCyberpunk2077Game.BuildCommandLineArgs(["-modded", "-skipStartScreen"], hasRedMods: true).Should().Equal("-modded", "-skipStartScreen");
    }
}
