using CliWrap;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Backend;
using NexusMods.CrossPlatform;
using NexusMods.Games.Generic;
using NexusMods.Games.Generic.Dependencies;
using NexusMods.Games.RedEngine.Cyberpunk2077;
using NexusMods.Games.RedEngine.Cyberpunk2077.Emitters;
using NexusMods.Games.TestFramework;
using NexusMods.Paths;
using NexusMods.Sdk;
using NexusMods.Sdk.Games;
using NexusMods.StandardGameLocators.TestHelpers;
using NSubstitute;
using Xunit.Abstractions;

namespace NexusMods.Games.RedEngine.Tests;

public class MissingProtontricksForRedModEmitterTests(ITestOutputHelper helper) : AIsolatedGameTest<MissingProtontricksForRedModEmitterTests, Cyberpunk2077Game>(helper)
{
    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddOSInterop()
            .AddRuntimeDependencies()
            .AddGenericGameSupport()
            .AddUniversalGameLocator<Cyberpunk2077Game>(new Version("1.61"), stores: [GameStore.Steam])
            .AddRedEngineGames();
    }

    /// <summary>
    /// An emitter that never finds protontricks, whatever the machine running the tests has installed.
    /// </summary>
    private static MissingProtontricksForRedModEmitter CreateEmitterWithoutProtontricks()
    {
        var processRunner = Substitute.For<IProcessRunner>();
        processRunner.RunAsync(Arg.Any<Command>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(exitCode: 1, DateTimeOffset.Now, DateTimeOffset.Now));

        var provider = new ServiceCollection()
            .AddSingleton(new AggregateProtontricksDependency(processRunner))
            .BuildServiceProvider();

        return new MissingProtontricksForRedModEmitter(provider);
    }

    private async Task AddRedModToGameFolder()
    {
        var redModFolder = GameInstallation.Locations.ToAbsolutePath(Constants.RedModInstallFolder).Combine("Driver_Shotguns");
        redModFolder.CreateDirectory();
        await redModFolder.Combine("info.json").WriteAllTextAsync("{}");
    }

    private async Task AddRedModToolToGameFolder()
    {
        var redModTool = GameInstallation.Locations.ToAbsolutePath(Constants.RedModPath);
        redModTool.Parent.CreateDirectory();
        await redModTool.WriteAllTextAsync("");
    }

    [Fact]
    public async Task SteamGameWithRedMods_WithoutProtontricks_TellsToInstallIt()
    {
        var loadout = await CreateLoadout();
        await AddRedModToGameFolder();
        await AddRedModToolToGameFolder();

        var diagnostics = await CreateEmitterWithoutProtontricks().Diagnose(loadout, CancellationToken.None).ToListAsync();

        diagnostics.Should().ContainSingle().Which.Title.Should().Be("Protontricks is not installed");
    }

    [Fact]
    public async Task WithoutRedMods_DoesNotNeedProtontricks()
    {
        var loadout = await CreateLoadout();
        await AddRedModToolToGameFolder();

        var diagnostics = await CreateEmitterWithoutProtontricks().Diagnose(loadout, CancellationToken.None).ToListAsync();

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutRedModTool_LeavesItToMissingRedModEmitter()
    {
        var loadout = await CreateLoadout();
        await AddRedModToGameFolder();

        var diagnostics = await CreateEmitterWithoutProtontricks().Diagnose(loadout, CancellationToken.None).ToListAsync();

        diagnostics.Should().BeEmpty();
    }
}
