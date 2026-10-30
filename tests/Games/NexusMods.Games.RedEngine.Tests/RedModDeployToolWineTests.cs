using CliWrap;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Backend;
using NexusMods.CrossPlatform;
using NexusMods.Games.Generic;
using NexusMods.Games.RedEngine.Cyberpunk2077;
using NexusMods.Games.TestFramework;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Loadouts;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;

namespace NexusMods.Games.RedEngine.Tests;

/// <summary>
/// REDmod of a game installed with Heroic or Lutris, which runs in the Wine prefix of the game.
/// </summary>
public class RedModDeployToolWineTests : AIsolatedGameTest<RedModDeployToolWineTests, Cyberpunk2077Game>, IDisposable
{
    // Field initializers run before the base constructor, which registers the services
    private readonly RecordingWinePrefixRunner _winePrefixRunner = new();

    public RedModDeployToolWineTests(ITestOutputHelper helper) : base(helper) { }

    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddOSInterop()
            .AddRuntimeDependencies()
            .AddGenericGameSupport()
            .AddUniversalGameLocator<Cyberpunk2077Game>(new Version("1.61"), stores: [GameStore.GOG], winePrefixRunner: _winePrefixRunner)
            .AddRedEngineGames();
    }

    public void Dispose()
    {
        if (_winePrefixRunner.WinePrefixDirectoryPath.DirectoryExists())
            _winePrefixRunner.WinePrefixDirectoryPath.DeleteDirectory(recursive: true);
    }

    private RedModDeployTool Tool => ServiceProvider.GetServices<ITool>().OfType<RedModDeployTool>().Single();

    private AbsolutePath PrefixLoadOrderFile => _winePrefixRunner.WinePrefixDirectoryPath.Combine("drive_c/modlist.txt");

    private AbsolutePath DeployScript => _winePrefixRunner.WinePrefixDirectoryPath.Combine("drive_c/redmod-deploy.bat");

    [Fact]
    public async Task RunsRedModInThePrefixOfTheGame()
    {
        var loadout = await CreateLoadout();

        await Tool.Execute(loadout, CancellationToken.None);

        var redMod = GameInstallation.Locations.ToAbsolutePath(Constants.RedModPath);
        var gameFolder = GameInstallation.Locations.ToAbsolutePath(new GamePath(LocationId.Game, RelativePath.Empty));
        var command = _winePrefixRunner.RunCommand;
        command.Should().NotBeNull();
        command!.TargetFilePath.Should().Be(DeployScript.ToString());
        command.WorkingDirPath.Should().Be(gameFolder.ToString());
        PrefixLoadOrderFile.FileExists.Should().BeTrue();

        // The game folder of the test is outside the prefix: redmod sees it through Z:
        var root = "Z:" + gameFolder.ToString().Replace('/', '\\');
        (await DeployScript.ReadAllTextAsync()).Should().Contain($"\"Z:{redMod.ToString().Replace('/', '\\')}\" deploy -force -modlist=C:\\modlist.txt -root=\"{root}\"");
    }

    [Fact]
    public async Task WithoutTheWineBuild_SkipsRedMod()
    {
        _winePrefixRunner.HasWine = false;
        var loadout = await CreateLoadout();

        await Tool.Execute(loadout, CancellationToken.None);

        _winePrefixRunner.RunCommand.Should().NotBeNull();
        PrefixLoadOrderFile.FileExists.Should().BeFalse();
        DeployScript.FileExists.Should().BeFalse();
    }

    private sealed class RecordingWinePrefixRunner : IWinePrefixRunner
    {
        public AbsolutePath WinePrefixDirectoryPath { get; } = Paths.FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"redmod-prefix-{Guid.NewGuid():N}");

        public bool HasWine { get; set; } = true;

        public Command? RunCommand { get; private set; }

        public Command? MakeRunCommand(Command command)
        {
            RunCommand = command;
            // A command that succeeds, in place of Wine
            return HasWine ? Cli.Wrap("true") : null;
        }
    }
}

public class RedModWinePathTests
{
    private static readonly AbsolutePath Prefix = FileSystem.Shared.FromUnsanitizedFullPath("/home/user/Games/gog/cyberpunk-2077");

    [Fact]
    public void GameInDriveC_IsOnDriveC()
    {
        var gameFolder = Prefix.Combine("drive_c/GOG Games/Cyberpunk 2077");

        RedModDeployTool.ToWinePath(gameFolder, Prefix).Should().Be(@"C:\GOG Games\Cyberpunk 2077");
    }

    [Fact]
    public void GameOutsideThePrefix_IsOnDriveZ()
    {
        var gameFolder = FileSystem.Shared.FromUnsanitizedFullPath("/home/user/Games/Heroic/Cyberpunk 2077");

        RedModDeployTool.ToWinePath(gameFolder, Prefix).Should().Be(@"Z:\home\user\Games\Heroic\Cyberpunk 2077");
    }

    [Fact]
    public async Task DeployScript_RunsRedModWithTheQuotedRoot()
    {
        // redmod ignores "-root=<folder>": the quotes must come after the =
        var prefix = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"redmod-script-{Guid.NewGuid():N}");
        try
        {
            var gameFolder = prefix.Combine("drive_c/GOG Games/Cyberpunk 2077");

            var script = await RedModDeployTool.WriteDeployScript(prefix, gameFolder, gameFolder.Combine("tools/redmod/bin/redMod.exe"), CancellationToken.None);

            script.Should().Be(prefix.Combine("drive_c/redmod-deploy.bat"));
            (await script.ReadAllTextAsync()).Should().Be(
                "@echo off\r\n" +
                "cd /d \"C:\\GOG Games\\Cyberpunk 2077\"\r\n" +
                "\"C:\\GOG Games\\Cyberpunk 2077\\tools\\redmod\\bin\\redMod.exe\" deploy -force -modlist=C:\\modlist.txt -root=\"C:\\GOG Games\\Cyberpunk 2077\"\r\n" +
                "exit /b %ERRORLEVEL%\r\n");
        }
        finally
        {
            if (prefix.DirectoryExists()) prefix.DeleteDirectory(recursive: true);
        }
    }
}
