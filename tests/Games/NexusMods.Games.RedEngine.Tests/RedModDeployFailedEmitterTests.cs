using FluentAssertions;
using NexusMods.Abstractions.Diagnostics;
using NexusMods.Games.RedEngine.Cyberpunk2077;
using NexusMods.Games.RedEngine.Cyberpunk2077.Emitters;
using NexusMods.Games.TestFramework;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using Xunit.Abstractions;

namespace NexusMods.Games.RedEngine.Tests;

public class RedModDeployFailedEmitterTests(ITestOutputHelper helper) : ACyberpunkIsolatedGameTest<RedModDeployFailedEmitterTests>(helper)
{
    // REDmodLog.txt of a failed deploy, from a real run on Cyberpunk 2077 2.5
    private static readonly string FailedLog = string.Join("\r\n",
        "\"C:\\GOG Games\\Cyberpunk 2077\\tools\\redmod\\bin\\redMod.exe\" deploy -force -modlist=C:\\modlist.txt -root=\"C:\\GOG Games\\Cyberpunk 2077\"",
        "[DEPLOY] Stage 1/5 - Initialization",
        "Found mod \"SidewinderMagSize\" (v2.5) in folder \"SidewinderMagSize\" (enabled; not deployed; TWEAKS; )",
        "Needs deployment: true (FORCE)",
        "[DEPLOY] Stage 2/5 - Script Compilation",
        "ERROR: Script compilation has failed!",
        "",
        "Commandlet deploy has failed.",
        "");

    // REDmodLog.txt of a deploy that didn't fail
    private static readonly string LogWithoutFailure = string.Join("\r\n",
        "[DEPLOY] Stage 1/5 - Initialization",
        "Found mod \"SidewinderMagSize\" (v2.5) in folder \"SidewinderMagSize\" (enabled; not deployed; TWEAKS; )",
        "Needs deployment: true (FORCE)",
        "[DEPLOY] Stage 2/5 - Script Compilation",
        "");

    private RedModDeployFailedEmitter Emitter => Game.DiagnosticEmitters.OfType<RedModDeployFailedEmitter>().Single();

    private AbsolutePath GameFolder => GameInstallation.Locations.ToAbsolutePath(new GamePath(LocationId.Game, RelativePath.Empty));

    private AbsolutePath RedModTool => GameInstallation.Locations.ToAbsolutePath(Constants.RedModPath);

    private async Task AddRedModToGameFolder()
    {
        var redModFolder = GameInstallation.Locations.ToAbsolutePath(Constants.RedModInstallFolder).Combine("SidewinderMagSize");
        redModFolder.CreateDirectory();
        await redModFolder.Combine("info.json").WriteAllTextAsync("{}");
    }

    private async Task AddRedModToolToGameFolder()
    {
        RedModTool.Parent.CreateDirectory();
        await RedModTool.WriteAllTextAsync("");
    }

    private static async Task WriteLog(AbsolutePath folder, string content, DateTime lastWriteTimeUtc)
    {
        var log = folder.Combine(RedModDeployFailedEmitter.LogFileName);
        await log.WriteAllTextAsync(content);
        File.SetLastWriteTimeUtc(log.ToString(), lastWriteTimeUtc);
    }

    [Fact]
    public async Task FailedDeploy_TellsTheErrorAndTheLog()
    {
        var loadout = await CreateLoadout();
        await AddRedModToGameFolder();
        await AddRedModToolToGameFolder();
        await WriteLog(GameFolder, FailedLog, DateTime.UtcNow);

        var diagnostics = await Emitter.Diagnose(loadout, CancellationToken.None).ToListAsync();

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Title.Should().Be("REDmod failed to deploy the REDmods");
        var messageData = diagnostic.Should().BeOfType<Diagnostic<Diagnostics.RedModDeployFailedMessageData>>().Which.MessageData;
        messageData.Error.Should().Be("Script compilation has failed!");
        messageData.LogPath.Should().Be(GameFolder.Combine(RedModDeployFailedEmitter.LogFileName).ToString());
    }

    [Fact]
    public async Task DeployWithoutFailure_SaysNothing()
    {
        var loadout = await CreateLoadout();
        await AddRedModToGameFolder();
        await AddRedModToolToGameFolder();
        await WriteLog(GameFolder, LogWithoutFailure, DateTime.UtcNow);

        var diagnostics = await Emitter.Diagnose(loadout, CancellationToken.None).ToListAsync();

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyTheLatestLogCounts()
    {
        var loadout = await CreateLoadout();
        await AddRedModToGameFolder();
        await AddRedModToolToGameFolder();

        // An old failed run from the folder of redmod, then a newer run without failure from the game folder
        await WriteLog(RedModTool.Parent, FailedLog, DateTime.UtcNow.AddHours(-1));
        await WriteLog(GameFolder, LogWithoutFailure, DateTime.UtcNow);
        (await Emitter.Diagnose(loadout, CancellationToken.None).ToListAsync()).Should().BeEmpty();

        // On Windows, redmod writes its log in its own folder
        await WriteLog(RedModTool.Parent, FailedLog, DateTime.UtcNow.AddHours(1));
        (await Emitter.Diagnose(loadout, CancellationToken.None).ToListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task WithoutRedMods_SaysNothing()
    {
        // The synchronizer doesn't run redmod without REDmods: its last log is outdated
        var loadout = await CreateLoadout();
        await AddRedModToolToGameFolder();
        await WriteLog(GameFolder, FailedLog, DateTime.UtcNow);

        var diagnostics = await Emitter.Diagnose(loadout, CancellationToken.None).ToListAsync();

        diagnostics.Should().BeEmpty();
    }
}
