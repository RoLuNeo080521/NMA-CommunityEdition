using DynamicData;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Library.Tests.DownloadsService.Helpers;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.NexusModsApi;
using Xunit;
using SyncHelpers = NexusMods.Library.Tests.DownloadsService.Helpers.SynchronizationHelpers;

namespace NexusMods.Library.Tests.DownloadsService;

/// <summary>
/// Regression test: a download must be completed as soon as its Nexus Mods job completes, even when the
/// HTTP job's Completed status hasn't been observed yet. In its own class, so it gets its own container and
/// service: completed downloads stay in the service and would break the tests expecting an empty one.
/// </summary>
public class DownloadsServiceCompletionTests(
    IJobMonitor jobMonitor,
    Library.DownloadsService service,
    IServiceProvider serviceProvider)
{
    private readonly DownloadJobFactory _jobFactory = new(jobMonitor, serviceProvider);

    [Fact]
    public async Task CompletedJob_IsCompleted_EvenIfHttpJobStatusArrivesLate()
    {
        // Arrange
        using var completedSubscription = service.CompletedDownloads.Bind(out var completedDownloads).Subscribe();
        using var activeSubscription = service.ActiveDownloads.Bind(out var activeDownloads).Subscribe();

        var context = _jobFactory.CreateAndStartDownloadJob(NexusModsGameId.From(1234u));
        context.WaitForJobsReady(TimeSpan.FromSeconds(30))
            .Should().BeTrue("jobs should signal ready within timeout");
        context.SignalJobsToStart();
        (await SyncHelpers.WaitForCollectionCount(activeDownloads, 1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("job should be in ActiveDownloads when started");

        // Act: the Nexus job completes and leaves the JobMonitor while its HTTP job hasn't reported Completed yet.
        context.CompletionSource.TrySetResult(context.NexusJob.Destination);
        await context.JobTask.Job.WaitAsync();

        // Assert
        (await SyncHelpers.WaitForCollectionCount(completedDownloads, 1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("the download should be completed once its Nexus job completed");
        (await SyncHelpers.WaitForCollectionCount(activeDownloads, 0, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("a completed download should not stay active");

        // Let the HTTP job finish too
        context.HttpCompletionSource.TrySetResult(context.HttpJob.Destination);
        await context.HttpJobTask.Job.WaitAsync();
    }

    // Nested Startup class for Xunit.DependencyInjection, giving this class its own container
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services) => DIHelpers.ConfigureServices(services);
    }
}
