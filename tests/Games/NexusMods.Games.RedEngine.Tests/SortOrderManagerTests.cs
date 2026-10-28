using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.RedEngine.Cyberpunk2077.SortOrder;
using NexusMods.Games.TestFramework;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using NexusMods.Sdk.Loadouts;
using Xunit.Abstractions;

namespace NexusMods.Games.RedEngine.Tests;

public class SortOrderManagerTests(ITestOutputHelper outputHelper) : ACyberpunkIsolatedGameTest<SortOrderManagerTests>(outputHelper)
{
    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddSingleton<RedModSortOrderVariety>(provider => new BlockingRedModSortOrderVariety(provider));
    }

    [Fact]
    public async Task Dispose_WaitsForTheRunningUpdate()
    {
        // The connection is disposed after the manager: an update still running then reads a closed database,
        // which crashes the process in RocksDB
        var variety = (BlockingRedModSortOrderVariety)ServiceProvider.GetRequiredService<RedModSortOrderVariety>();
        var sortOrderManager = (SortOrderManager)InitAndGetSortOrderManager();

        var loadout = await CreateLoadout();
        await using var tempDir = TemporaryFileManager.CreateFolder();
        var archivePath = tempDir.Path.Combine("one_mod.7z");
        File.Copy(FileSystem.GetKnownPath(KnownPath.EntryDirectory).Combine("LibraryArchiveInstallerTests/Resources/one_mod.7z").ToString(), archivePath.ToString());
        var libraryArchive = await RegisterLocalArchive(archivePath);
        await LoadoutManager.InstallItem(libraryArchive.AsLibraryFile().AsLibraryItem(), loadout);

        await variety.ReconcileStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var dispose = Task.Run(sortOrderManager.Dispose);
        var first = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromMilliseconds(500)));
        first.Should().NotBeSameAs(dispose, "Dispose must wait for the update that is still running");

        variety.ReleaseReconcile.SetResult();
        await dispose.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private class BlockingRedModSortOrderVariety(IServiceProvider serviceProvider) : RedModSortOrderVariety(serviceProvider)
    {
        public TaskCompletionSource ReconcileStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseReconcile { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask ReconcileSortOrder(SortOrderId sortOrderId, IDb? referenceDb = null, CancellationToken token = default)
        {
            ReconcileStarted.TrySetResult();

            // Stands for database work that doesn't watch the token
            await ReleaseReconcile.Task;
            await base.ReconcileSortOrder(sortOrderId, referenceDb, token);
        }
    }
}
