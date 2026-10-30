using CliWrap;
using GameFinder.Wine;
using NexusMods.Backend.Games.Locators;
using NexusMods.Paths;

namespace NexusMods.Backend.Tests.Games;

public class WinePrefixRunnerTests : IDisposable
{
    private readonly AbsolutePath _home;
    private readonly IFileSystem _fileSystem;

    public WinePrefixRunnerTests()
    {
        _home = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory).Combine($"wine-runner-tests-{Guid.NewGuid():N}");
        _home.CreateDirectory();

        _fileSystem = FileSystem.Shared.CreateOverlayFileSystem(
            pathMappings: new Dictionary<AbsolutePath, AbsolutePath>(),
            knownPathMappings: new Dictionary<KnownPath, AbsolutePath>
            {
                { KnownPath.HomeDirectory, _home },
                { KnownPath.XDG_DATA_HOME, _home.Combine(".local/share") },
            }
        );
    }

    public void Dispose()
    {
        if (_home.DirectoryExists()) _home.DeleteDirectory(recursive: true);
    }

    private AbsolutePath PrefixFolder => _home.Combine("Games/cyberpunk-2077");

    private AbsolutePath LutrisData => _home.Combine(".local/share/lutris");

    private AbsolutePath RedMod => _home.Combine("Games/Cyberpunk 2077/tools/redmod/bin/redMod.exe");

    private Command RedModCommand => Cli.Wrap(RedMod.ToString())
        .WithArguments(["deploy", "-force", "-modlist=C:\\modlist.txt"])
        .WithWorkingDirectory(RedMod.Parent.ToString())
        .WithEnvironmentVariables(env => env.Set("DXVK_LOG_LEVEL", "none"));

    private WinePrefix Prefix => new() { ConfigurationDirectory = PrefixFolder, UserName = "user" };

    private async Task<AbsolutePath> CreateFile(AbsolutePath file)
    {
        file.Parent.CreateDirectory();
        await file.WriteAllTextAsync("");
        return file;
    }

    private LutrisWinePrefixRunner CreateLutrisRunner(string? wineVersion, string? customWinePath = null) => new(_fileSystem, PrefixFolder, Prefix, wineVersion, customWinePath, LutrisData)
    {
        SearchPath = "",
        SystemUmuDirectories = [],
    };

    private async Task AssertRunsRedMod(Command? command, AbsolutePath runner, string? verb, Dictionary<string, string> environment)
    {
        await Assert.That(command).IsNotNull();
        await Assert.That(command!.TargetFilePath).IsEqualTo(runner.ToString());
        await Assert.That(command.Arguments).IsEqualTo($"{(verb is null ? "" : verb + " ")}\"{RedMod}\" deploy -force -modlist=C:\\modlist.txt");
        await Assert.That(command.WorkingDirPath).IsEqualTo(RedMod.Parent.ToString());
        await Assert.That(command.EnvironmentVariables["DXVK_LOG_LEVEL"]).IsEqualTo("none");
        foreach (var (name, value) in environment)
            await Assert.That(command.EnvironmentVariables[name]).IsEqualTo(value);
    }

    [Test]
    public async Task Test_Heroic_WineBuild_RunsTheProgramInWinePrefix()
    {
        var wine = await CreateFile(_home.Combine(".config/heroic/tools/wine/Wine-GE-Proton8-26/bin/wine"));

        var command = HeroicWinePrefixRunner.Create(_fileSystem, PrefixFolder, Prefix, wine.ToString(), "wine")!.MakeRunCommand(RedModCommand);

        await AssertRunsRedMod(command, wine, verb: null, new() { ["WINEPREFIX"] = PrefixFolder.ToString() });
    }

    [Test]
    public async Task Test_Heroic_ProtonBuild_RunsTheProgramWithProtonRun()
    {
        var proton = await CreateFile(_home.Combine(".config/heroic/tools/proton/GE-Proton9-20/proton"));

        var command = HeroicWinePrefixRunner.Create(_fileSystem, PrefixFolder, Prefix, $"'{proton}'", "proton")!.MakeRunCommand(RedModCommand);

        await AssertRunsRedMod(command, proton, verb: "run", new()
        {
            ["STEAM_COMPAT_DATA_PATH"] = PrefixFolder.ToString(),
            ["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = _home.Combine(".steam/steam").ToString(),
        });
    }

    [Test]
    public async Task Test_Heroic_WithoutTheWineBuild_RunsNothing()
    {
        var runner = HeroicWinePrefixRunner.Create(_fileSystem, PrefixFolder, Prefix, _home.Combine("missing/bin/wine").ToString(), "wine");

        await Assert.That(runner!.MakeRunCommand(RedModCommand)).IsNull();
        await Assert.That(HeroicWinePrefixRunner.Create(_fileSystem, PrefixFolder, Prefix, "/opt/cxoffice/bin/wine", "crossover")).IsNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("missing-wine-build")]
    public async Task Test_Lutris_DefaultVersion_RunsTheProgramWithUmuAndGeProton(string? wineVersion)
    {
        var umu = await CreateFile(LutrisData.Combine("runtime/umu/umu-run"));

        var command = CreateLutrisRunner(wineVersion).MakeRunCommand(RedModCommand);

        await AssertRunsRedMod(command, umu, verb: null, new()
        {
            ["WINEPREFIX"] = PrefixFolder.ToString(),
            ["GAMEID"] = "umu-default",
            ["PROTONPATH"] = "GE-Proton",
        });
    }

    [Test]
    public async Task Test_Lutris_ProtonBuild_RunsTheProgramWithUmuAndThisBuild()
    {
        var umu = await CreateFile(LutrisData.Combine("runtime/umu/umu-run"));
        var protonBuild = _home.Combine(".local/share/Steam/compatibilitytools.d/GE-Proton10-4");
        await CreateFile(protonBuild.Combine("proton"));

        var command = CreateLutrisRunner("GE-Proton10-4").MakeRunCommand(RedModCommand);

        await AssertRunsRedMod(command, umu, verb: null, new() { ["PROTONPATH"] = protonBuild.ToString() });
    }

    [Test]
    public async Task Test_Lutris_WineBuild_RunsTheProgramWithThisBuild()
    {
        var wine = await CreateFile(LutrisData.Combine("runners/wine/wine-ge-8-26-x86_64/bin/wine"));

        var command = CreateLutrisRunner("wine-ge-8-26-x86_64").MakeRunCommand(RedModCommand);

        await AssertRunsRedMod(command, wine, verb: null, new() { ["WINEPREFIX"] = PrefixFolder.ToString() });
    }

    [Test]
    public async Task Test_Lutris_SystemAndCustomWine()
    {
        var systemWine = await CreateFile(_home.Combine("usr/bin/wine"));
        var customWine = await CreateFile(_home.Combine("wine-custom/bin/wine"));

        // Without Wine in PATH, Lutris falls back to umu, which isn't installed here
        await Assert.That(CreateLutrisRunner("system").MakeRunCommand(RedModCommand)).IsNull();

        var runnerWithPath = new LutrisWinePrefixRunner(_fileSystem, PrefixFolder, Prefix, "system", null, LutrisData)
        {
            SearchPath = $"/nonexistent:{systemWine.Parent}",
            SystemUmuDirectories = [],
        };
        await AssertRunsRedMod(runnerWithPath.MakeRunCommand(RedModCommand), systemWine, verb: null, new() { ["WINEPREFIX"] = PrefixFolder.ToString() });

        await AssertRunsRedMod(CreateLutrisRunner("custom", customWine.ToString()).MakeRunCommand(RedModCommand), customWine, verb: null, new());
    }

    [Test]
    public async Task Test_Lutris_WithoutUmu_RunsNothing()
    {
        await Assert.That(CreateLutrisRunner(null).MakeRunCommand(RedModCommand)).IsNull();
    }
}
