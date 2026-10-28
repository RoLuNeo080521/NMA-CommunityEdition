using FluentAssertions;
using NexusMods.App.UI.WorkspaceAttachments;
using NexusMods.App.UI.WorkspaceSystem;
using NexusMods.Games.TestFramework;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Loadouts;
using Xunit.Abstractions;

namespace NexusMods.DataModel.Synchronizer.Tests;

/// <summary>
/// A managed game that isn't detected anymore (uninstalled, moved, or its launcher removed): the lists of loadouts
/// skip its loadouts instead of failing on <see cref="Loadout.ReadOnly.InstallationInstance"/>.
/// </summary>
public class UndetectedGameTests(ITestOutputHelper helper) : ACyberpunkIsolatedGameTest<UndetectedGameTests>(helper)
{
    private async Task<Loadout.ReadOnly> MakeGameUndetected(Loadout.ReadOnly loadout)
    {
        // No locator reports the game from this store
        using var tx = Connection.BeginTransaction();
        tx.Add(loadout.InstallationId, GameInstallMetadata.Store, GameStore.GOG);
        await tx.Commit();

        return Loadout.Load(Connection.Db, loadout.LoadoutId);
    }

    [Fact]
    public async Task LoadoutOfAnUndetectedGameIsSkipped()
    {
        var loadout = await CreateLoadout();
        loadout.IsGameDetected().Should().BeTrue();

        loadout = await MakeGameUndetected(loadout);

        loadout.IsGameDetected().Should().BeFalse();
        FluentActions.Invoking(() => loadout.InstallationInstance).Should().Throw<Exception>();
        Loadout.All(Connection.Db).Where(l => l.IsVisible() && l.IsGameDetected()).Should().BeEmpty();
    }

    [Fact]
    public async Task WorkspaceOfAnUndetectedGameIsNotRestored()
    {
        var loadout = await CreateLoadout();
        var context = new LoadoutContext { LoadoutId = loadout.LoadoutId };
        context.IsValid(ServiceProvider).Should().BeTrue();

        loadout = await MakeGameUndetected(loadout);

        context.IsValid(ServiceProvider).Should().BeFalse("restoring the workspace of this loadout would fail");
        new LoadoutAttachmentsFactory(Connection).CreateTitle(context).Should().Be(loadout.Game.DisplayName);
    }
}
