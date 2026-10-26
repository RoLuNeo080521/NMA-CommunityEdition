using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Sdk.Settings;

namespace NexusMods.Sdk.Tracking;

/// <remarks>
/// The tracking sends its data to Nexus Mods, so the community edition doesn't offer it: the setting isn't shown and
/// the app ignores it at startup.
/// </remarks>
public record TrackingSettings : ISettings
{
    public bool EnableTracking { get; [UsedImplicitly] set; }

    public Guid DeviceId { get; set; }

    public static ISettingsBuilder Configure(ISettingsBuilder settingsBuilder)
    {
        return settingsBuilder
            .ConfigureBackend(StorageBackendOptions.Use(StorageBackends.Json))
            .ConfigureDefault(CreateDefault);
    }

    private static TrackingSettings CreateDefault(IServiceProvider serviceProvider)
    {
        var timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

        return new TrackingSettings
        {
            EnableTracking = false,
            DeviceId = Guid.CreateVersion7(timeProvider.GetUtcNow()),
        };
    }
}
