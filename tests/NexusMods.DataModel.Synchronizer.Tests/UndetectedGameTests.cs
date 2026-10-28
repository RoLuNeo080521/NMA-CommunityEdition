using FluentAssertions;
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
    [Fact]
    public async Task LoadoutOfAnUndetectedGameIsSkipped()
    {
        var loadout = await CreateLoadout();
        loadout.IsGameDetected().Should().BeTrue();

        // No locator reports the game from this store
        using (var tx = Connection.BeginTransaction())
        {
            tx.Add(loadout.InstallationId, GameInstallMetadata.Store, GameStore.GOG);
            await tx.Commit();
        }

        loadout = Loadout.Load(Connection.Db, loadout.LoadoutId);
        loadout.IsGameDetected().Should().BeFalse();
        FluentActions.Invoking(() => loadout.InstallationInstance).Should().Throw<Exception>();
        Loadout.All(Connection.Db).Where(l => l.IsVisible() && l.IsGameDetected()).Should().BeEmpty();
    }
}
