using System.Collections.Frozen;
using DynamicData;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Sdk.Loadouts;
using NexusMods.Sdk.NexusModsApi;
using R3;
using OneOf;

namespace NexusMods.Abstractions.Games;

/// <summary>
/// Implementation for ISortOrderManager
/// Responsible for subscribing and updating sortOrders for all registered varieties when loadouts or collections change.
/// </summary>
public class SortOrderManager : ISortOrderManager, IDisposable
{
    private readonly IServiceProvider _serviceProvider; 
    private readonly IConnection _connection;
    private IDisposable? _subscription;
    private readonly ILogger<SortOrderManager> _logger;
    
    // The change handlers that are running, plus one until Dispose. The connection is disposed after this manager,
    // so Dispose waits for them: a handler still reading the closed database would crash the process in RocksDB.
    private readonly CountdownEvent _runningHandlers = new(initialCount: 1);
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);
    private int _isDisposed;

    private FrozenDictionary<SortOrderVarietyId, ISortOrderVariety> _sortOrderVarieties;

    public SortOrderManager(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _connection = _serviceProvider.GetRequiredService<IConnection>();
        _sortOrderVarieties = FrozenDictionary<SortOrderVarietyId, ISortOrderVariety>.Empty;
        _logger = _serviceProvider.GetRequiredService<ILogger<SortOrderManager>>();
    }

    /// <inheritdoc />
    public async ValueTask UpdateLoadOrders(LoadoutId loadoutId, Optional<CollectionGroupId> collectionGroupId = default, CancellationToken token = default)
    {
        var parentEntity = collectionGroupId.HasValue
            ? OneOf<LoadoutId, CollectionGroupId>.FromT1(collectionGroupId.Value)
            : OneOf<LoadoutId, CollectionGroupId>.FromT0(loadoutId);
        
        foreach (var sortOrderVariety in _sortOrderVarieties.Values)
        {
            // Reconcile the sort order for each variety
            var sortOrderId = await sortOrderVariety.GetOrCreateSortOrderFor(loadoutId, parentEntity, token);
            await sortOrderVariety.ReconcileSortOrder(sortOrderId, token: token);
        }
    }
    
    protected async ValueTask CreateSortOrders(LoadoutId loadoutId, Optional<CollectionGroupId> collectionGroupId = default, CancellationToken token = default)
    {
        var parentEntity = collectionGroupId.HasValue
            ? OneOf<LoadoutId, CollectionGroupId>.FromT1(collectionGroupId.Value)
            : OneOf<LoadoutId, CollectionGroupId>.FromT0(loadoutId);
        
        foreach (var sortOrderVariety in _sortOrderVarieties.Values)
        {
            // Create the sort order for each variety
            _ = await sortOrderVariety.GetOrCreateSortOrderFor(loadoutId, parentEntity, token);
        }
    }

    protected async ValueTask DeleteSortOrders(LoadoutId loadoutId, Optional<CollectionGroupId> collectionGroupId = default, CancellationToken token = default)
    {
        var parentEntity = collectionGroupId.HasValue
            ? OneOf<LoadoutId, CollectionGroupId>.FromT1(collectionGroupId.Value)
            : OneOf<LoadoutId, CollectionGroupId>.FromT0(loadoutId);
        
        foreach (var sortOrderVariety in _sortOrderVarieties.Values)
        {
            // Delete the sort order for each variety
            var sortOrderId = sortOrderVariety.GetSortOrderIdFor(parentEntity);
            if (!sortOrderId.HasValue) continue;
            
            await sortOrderVariety.DeleteSortOrder(sortOrderId.Value, token: token);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ISortOrderVariety> GetSortOrderVarieties()
    {
        return _sortOrderVarieties.Values;
    }

    /// <inheritdoc />
    public void RegisterSortOrderVarieties(ISortOrderVariety[] sortOrderVarieties, IGame game)
    {
        _sortOrderVarieties = sortOrderVarieties.ToDictionary(variety => variety.SortOrderVarietyId)
            .ToFrozenDictionary();
        
        if (_sortOrderVarieties.Count > 0)
        {
            // Subscribe to changes in the sort orders
            SubscribeToChanges(game.NexusModsGameId.Value);
        }
    }

    protected void SubscribeToChanges(NexusModsGameId nexusModsGameId)
    {
        var compositeDisposable = new CompositeDisposable();
        var conn = _connection;
 
        
        // Listen to Loadouts additions/removals
        Loadout.ObserveAll(_connection)
            .StartWithEmpty()
            .FilterImmutable(l => l.Installation.GameId == nexusModsGameId)
            .ToObservable()
            .SubscribeAwait(this, static async (changes, state, token) =>
                {
                    using var running = state.TryEnterHandler();
                    if (running is null) return;

                    foreach (var change in changes)
                    {
                        var loadoutId = change.Current.LoadoutId;
                        var parentEntity = loadoutId;

                        switch (change.Reason)
                        {
                            case ChangeReason.Add:
                                // Create the sort order for this loadout
                                // Might not be needed if the other subscription to loadout items handles it
                                await state.CreateSortOrders(loadoutId, token: token);
                                break;
                            case ChangeReason.Update:
                                // If loadout changes, we handle that in a separate subscription
                                break;
                            case ChangeReason.Remove:
                                // Remove the orphaned sort orders
                                await state.DeleteSortOrders(loadoutId, token: token);
                                break;
                        }
                    }
                },
                // Run on the thread pool rather than the subscriber's context (UI thread, test runner): Dispose waits
                // for the handlers and could be blocking that context
                configureAwait: false
            )
            .AddTo(compositeDisposable);
        
        
        // Listen to collection groups additions/removals
        CollectionGroup.ObserveAll(_connection)
            .StartWithEmpty()
            .FilterImmutable(cg => cg.AsLoadoutItemGroup().AsLoadoutItem().Loadout.Installation.GameId == nexusModsGameId)
            .ToObservable()
            .SubscribeAwait(this, static async (changes, state, token) =>
                {
                    using var running = state.TryEnterHandler();
                    if (running is null) return;

                    foreach (var change in changes)
                    {
                        var collectionGroupId = change.Current.CollectionGroupId;
                        var loadoutId = change.Current.AsLoadoutItemGroup().AsLoadoutItem().LoadoutId;
                        var parentEntity = Optional.Some(collectionGroupId);

                        switch (change.Reason)
                        {
                            case ChangeReason.Add:
                                // Create the sort order for this collection group
                                // Might not be needed if the other subscription to loadout items handles it
                                await state.CreateSortOrders(loadoutId, parentEntity, token: token);
                                break;
                            case ChangeReason.Update:
                                // If collection group changes, we handle that in a separate subscription
                                break;
                            case ChangeReason.Remove:
                                // Remove the orphaned sort orders
                                await state.DeleteSortOrders(loadoutId, parentEntity, token: token);
                                break;
                        }
                    }
                },
                configureAwait: false
            )
            .AddTo(compositeDisposable);
        

        // Listen to item additions/removals
        // TODO: consider making this Variant specific, to only listen to changes for the relevant items
        // TODO: listen to changes to Game files too
        SortOrderQueries.TrackLoadoutItemChanges(_connection, nexusModsGameId)
            .ToObservable()
            .SubscribeAwait(this, static async (changes, state, token) =>
            {
                using var running = state.TryEnterHandler();
                if (running is null) return;

                // Filter out updates where nothing changed
                var filteredChanges = changes
                    .Where(change => change.Reason != ChangeReason.Update)
                    .ToList();
                if (filteredChanges.Count == 0) return;

                var loadouts = new HashSet<LoadoutId>();
                var collections = new Dictionary<CollectionGroupId, LoadoutId>();
                foreach (var change in filteredChanges)
                {
                    if (change.Reason == ChangeReason.Update)
                        continue;
                    
                    var loadoutId = change.Current.LoadoutId;
                    loadouts.Add(loadoutId);
                    
                    var collectionId = change.Current.CollectionId;
                    if (collectionId != 0)
                        collections[collectionId] = loadoutId;
                }
                
                // Update loadout sort orders
                foreach (var loadoutId in loadouts)
                {
                    await state.UpdateLoadOrders(loadoutId, token: token);
                }
                
                // Update collection sort orders
                foreach (var pair in collections)
                {
                    await state.UpdateLoadOrders(pair.Value, Optional.Some(pair.Key), token: token);
                }
            }, configureAwait: false)
            .AddTo(compositeDisposable);
        
        _subscription = compositeDisposable;
    }

    private HandlerScope? TryEnterHandler()
    {
        // Fails once Dispose ran and no handler is left
        return _runningHandlers.TryAddCount() ? new HandlerScope(_runningHandlers) : null;
    }

    private sealed class HandlerScope(CountdownEvent runningHandlers) : IDisposable
    {
        public void Dispose() => runningHandlers.Signal();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        // Cancels the token of the running handlers, but doesn't wait for them
        _subscription?.Dispose();

        _runningHandlers.Signal();
        if (!_runningHandlers.Wait(DisposeTimeout))
            _logger.LogWarning("Sort order updates still running after {Timeout}, disposing anyway", DisposeTimeout);
    }
}


