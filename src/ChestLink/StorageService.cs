using StardewModdingAPI;
using StardewValley;
using SObject = StardewValley.Object;
using System.Diagnostics;

namespace ChestLink;

internal sealed class StorageService(ModEntry mod)
{
    private const int RetryAfterTicks = 180;
    private const int WarnAfterTicks = 600;
    private const int MaxCachedResults = 32;
    private PendingTransfer? pending;
    private readonly Dictionary<long, Dictionary<string, Item>> remoteItems = new();
    private readonly HashSet<string> appliedRequestIds = new();
    private readonly Queue<string> appliedRequestOrder = new();
    private readonly Dictionary<long, RequestResultCache> hostResults = new();
    private StorageSnapshot? latestSnapshot;
    private ChestCatalog? cachedCatalog;
    private long snapshotRevision;
    private bool subscribedToStorage;
    private bool chestInventoryChanged;
    private int suppressChestEventsThroughTick = int.MinValue;

    internal ChestCatalog GetCatalog() => cachedCatalog ??= ChestCatalog.Read();

    private ChestCatalog RefreshCatalog() => cachedCatalog = ChestCatalog.Read();

    internal void Transfer(StorageMenuModel model, StorageSlot slot, int requested)
    {
        mod.LogInteraction($"transfer entry: {(slot.InStorage ? "withdraw" : "deposit")}, " +
            $"item={slot.Item?.QualifiedItemId ?? "<empty>"}, requested={requested}, busy={pending is not null}");
        if (slot.Item is null || requested <= 0 || pending is not null) return;
        if (!Context.IsMainPlayer)
        {
            StorageRequest request = new()
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Action = slot.InStorage ? "withdraw" : "deposit",
                ItemKey = slot.RemoteKey,
                Count = requested,
            };
            var localItems = new List<PendingItem>();
            if (!slot.InStorage)
            {
                int index = slot.BackpackIndex;
                Item? current = index >= 0 && index < Game1.player.Items.Count
                    ? Game1.player.Items[index] : null;
                if (current is null || !ChestCatalog.SameStack(current, slot.Item)) return;
                int amount = Math.Min(requested, current.Stack);
                Item transfer = CopyWithStack(current, amount);
                Item? remainder = current.Stack == amount
                    ? null : CopyWithStack(current, current.Stack - amount);
                string data;
                try { data = ItemNetCodec.Encode(transfer); }
                catch (Exception error)
                {
                    mod.Monitor.Log($"Couldn't serialize local deposit item: {error}", LogLevel.Warn);
                    model.SetStatus(mod.T("status.transaction-failed"));
                    return;
                }
                request.Count = amount;
                request.Items.Add(new StorageRequestItem { ItemData = data, Count = amount });
                localItems.Add(new PendingItem(transfer, amount));
                SetBackpackSlot(index, remainder);
                model.Refresh();
            }
            else
            {
                int capacity = Capacity(Game1.player.Items, Game1.player.MaxItems, slot.Item);
                int emptySlots = Game1.player.Items.Count(item => item is null);
                int matchingStackCapacity = Game1.player.Items
                    .Where(item => item is not null && ChestCatalog.SameStack(item, slot.Item))
                    .Sum(item => Math.Max(0, item!.maximumStackSize() - item.Stack));
                mod.LogInteraction($"client withdraw precheck: item={slot.Item.QualifiedItemId}, " +
                    $"requested={requested}, capacity={capacity}, emptySlots={emptySlots}, " +
                    $"matchingStackCapacity={matchingStackCapacity}, itemSlots={Game1.player.Items.Count}, " +
                    $"maxItems={Game1.player.MaxItems}, key={slot.RemoteKey}");
                if (capacity <= 0)
                {
                    model.SetStatus(mod.T("status.no-space"));
                    return;
                }
                request.Count = Math.Min(requested, capacity);
            }
            model.SetStatus("");
            BeginRemoteRequest(request, model, localItems);
            return;
        }
        mod.Monitor.Log($"Transfer requested: {(slot.InStorage ? "withdraw" : "deposit")} " +
            $"{slot.Item.QualifiedItemId} x{requested}", LogLevel.Trace);
        Execute(model, catalog =>
        {
            var matchingSources = catalog.FindGroups(slot.Item)
                .SelectMany(group => group.Sources).ToList();
            if (slot.InStorage && matchingSources.Count > 0 &&
                matchingSources.All(source => !CanUse(source.Chest)))
                return mod.T("status.chest-busy");
            var destinations = catalog.Chests.Where(CanUse).ToList();
            if (!slot.InStorage && destinations.Count == 0 && catalog.Chests.Count > 0)
                return mod.T("status.chest-busy");
            int moved = slot.InStorage
                ? Withdraw(catalog, Game1.player, slot.Item, requested)
                : Deposit(catalog, Game1.player, slot.BackpackIndex, slot.Item, requested, destinations);
            mod.LogInteraction($"transfer result: moved={moved}, item={slot.Item.QualifiedItemId}");
            if (moved > 0) Game1.playSound(slot.InStorage ? "coin" : "dwop");
            return moved > 0 ? "" : mod.T("status.no-space");
        });
    }

    internal void QuickStack(StorageMenuModel? model)
    {
        if (pending is not null) return;
        if (!Context.IsMainPlayer)
        {
            if (latestSnapshot is null)
            {
                string id = Guid.NewGuid().ToString("N");
                pending = PendingTransfer.WaitingForSnapshot(id, model);
                mod.SendStorageSnapshotRequest(new StorageSnapshotRequest
                    { RequestId = id, UseAutoStoreRange = true });
                return;
            }
            SubmitRemoteQuickStack(model);
            return;
        }
        Execute(model, catalog =>
        {
            var transfers = new List<(ChestSource Chest, Item Item)>();
            int total = QuickStack(catalog, Game1.player, transfers);
            if (total > 0)
            {
                Game1.playSound("Ship");
                PlayAnimationSafely(Game1.player, transfers);
            }
            return total > 0 ? "" : mod.T("status.nothing-to-stack");
        }, useAutoStoreRange: true);
    }

    internal void QuickStackNearChests(StorageMenuModel? model)
    {
        if (pending is not null) return;
        if (!Context.IsMainPlayer)
        {
            SubmitRemoteQuickStack(model, useNearbyChests: true);
            return;
        }
        Execute(model, catalog =>
        {
            IReadOnlyList<ChestSource> nearby = mod.GetNearbySupportedChests(Game1.player, catalog);
            var transfers = new List<(ChestSource Chest, Item Item)>();
            int total = QuickStack(catalog, Game1.player, transfers,
                allowedChests: nearby.Select(source => source.Chest).ToHashSet());
            if (total > 0)
            {
                Game1.playSound("Ship");
                PlayAnimationSafely(Game1.player, transfers);
            }
            return total > 0 ? "" : mod.T("status.nothing-to-stack");
        }, useNearbyChestRange: true);
    }

    internal void RequestSnapshot(long playerId, StorageSnapshotRequest request)
    {
        if (!Context.IsMainPlayer) return;
        Farmer? player = Game1.getOnlineFarmers()
            .FirstOrDefault(farmer => farmer.UniqueMultiplayerID == playerId);
        if (player is null) return;
        bool inRange = request.UseAutoStoreRange
            ? mod.IsInAutoStoreRange(player) : mod.IsNearTerminal(player);
        if (!inRange)
        {
            mod.SendStorageResult(playerId, new StorageResult
            {
                RequestId = request.RequestId,
                Action = "snapshot",
                Status = mod.T("status.too-far")
            });
            return;
        }
        if (request.Subscribe) watchingStorage.Add(playerId);
        SendSnapshotReply(playerId, request.RequestId,
            BuildSnapshot(playerId, GetCatalog()));
    }

    private readonly HashSet<long> watchingStorage = new();

    internal void StopWatchingStorage()
    {
        if (Context.IsMainPlayer) return;
        if (subscribedToStorage) mod.SendStorageUnsubscribe();
        subscribedToStorage = false;
    }

    internal bool IsWatchingStorage => subscribedToStorage;

    internal void HandleUnsubscribe(long playerId) => watchingStorage.Remove(playerId);

    internal void NotifyChestInventoryChanged()
    {
        if (Game1.ticks <= suppressChestEventsThroughTick) return;
        cachedCatalog = null;
        chestInventoryChanged = true;
    }

    internal void NotifyChestObjectsChanged()
    {
        cachedCatalog = null;
        chestInventoryChanged = true;
    }

    private void MarkStorageMutationSynchronized(ChestCatalog catalog)
    {
        cachedCatalog = catalog;
        chestInventoryChanged = false;
        suppressChestEventsThroughTick = Game1.ticks + 1;
    }

    internal void HandleRemoteRequest(long playerId, StorageRequest request)
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady) return;
        var stopwatch = Stopwatch.StartNew();
        long catalogReadMs = 0;
        long operationMs = 0;
        long updateMs = 0;
        Farmer? player = Game1.getOnlineFarmers()
            .FirstOrDefault(farmer => farmer.UniqueMultiplayerID == playerId);
        if (player is null) return;
        if (string.IsNullOrWhiteSpace(request.RequestId)) return;
        mod.LogInteraction($"host received request id={request.RequestId}, player={playerId}, " +
            $"action={request.Action}, key={request.ItemKey}, count={request.Count}");
        if (TryGetCachedResult(playerId, request.RequestId, out StorageResult? cached))
        {
            mod.SendStorageResult(playerId, cached);
            SendSnapshot(playerId, GetCatalog());
            return;
        }
        var result = new StorageResult { RequestId = request.RequestId, Action = request.Action };
        var transfers = new List<(ChestSource Chest, Item Item)>();
        ChestCatalog? updatedCatalog = null;
        try
        {
            bool terminalQuickStack = request.Action == "quick-stack" && mod.IsInAutoStoreRange(player);
            bool chestQuickStack = request.Action == "quick-stack-nearby" && mod.IsNearSupportedChest(player);
            if (!mod.IsNearTerminal(player) && !terminalQuickStack && !chestQuickStack)
                result.Status = mod.T("status.too-far");
            else
            {
                long phaseStart = stopwatch.ElapsedMilliseconds;
                ChestCatalog catalog = GetCatalog();
                catalogReadMs += stopwatch.ElapsedMilliseconds - phaseStart;
                phaseStart = stopwatch.ElapsedMilliseconds;
                switch (request.Action)
                {
                    case "withdraw":
                        bool hasPlayerKeys = remoteItems.TryGetValue(playerId, out var keyMap);
                        Item? withdrawExemplar = null;
                        bool keyFound = hasPlayerKeys && keyMap!.TryGetValue(request.ItemKey, out withdrawExemplar);
                        if (keyFound)
                        {
                            var matching = catalog.FindGroups(withdrawExemplar!)
                                .SelectMany(group => group.Sources).ToList();
                            int available = matching.Sum(source => source.Item.Stack);
                            mod.LogInteraction($"host withdraw lookup: item={withdrawExemplar!.QualifiedItemId}, " +
                                $"key={request.ItemKey}, keyFound=true, keys={keyMap!.Count}, " +
                                $"sources={matching.Count}, available={available}, requested={request.Count}");
                            if (matching.Count > 0 && matching.All(source => !CanUse(source.Chest)))
                                result.Status = mod.T("status.chest-busy");
                            else
                            {
                                result.Items = RemoteWithdraw(playerId, catalog, request,
                                    out int withdrawn);
                                result.MovedCount = withdrawn;
                                if (withdrawn == 0)
                                    result.Status = mod.T("status.storage-updated");
                            }
                        }
                        else
                        {
                            int cachedKeyCount = hasPlayerKeys ? keyMap!.Count : 0;
                            mod.LogInteraction($"host withdraw lookup: keyFound=false, " +
                                $"hasPlayerKeys={hasPlayerKeys}, keys={cachedKeyCount}, " +
                                $"key={request.ItemKey}, requested={request.Count}");
                            result.Status = mod.T("status.storage-updated");
                        }
                        break;
                    case "deposit":
                        if (catalog.Chests.Count > 0 && catalog.Chests.All(chest => !CanUse(chest)))
                            result.Status = mod.T("status.chest-busy");
                        else
                        {
                            result.AcceptedCounts = RemoteDeposit(catalog, request, transfers);
                            result.MovedCount = result.AcceptedCounts.Sum();
                        }
                        break;
                    case "quick-stack":
                        result.AcceptedCounts = RemoteQuickStack(catalog, request, transfers);
                        result.MovedCount = result.AcceptedCounts.Sum();
                        break;
                    case "quick-stack-nearby":
                        var nearbyChests = mod.GetNearbySupportedChests(player, catalog)
                            .Select(source => source.Chest).ToHashSet();
                        result.AcceptedCounts = RemoteQuickStack(catalog, request, transfers, nearbyChests);
                        result.MovedCount = result.AcceptedCounts.Sum();
                        break;
                    default:
                        result.Status = mod.T("status.transaction-failed");
                        break;
                }
                operationMs += stopwatch.ElapsedMilliseconds - phaseStart;
                if (result.MovedCount == 0 && result.Status.Length == 0)
                    result.Status = mod.T(IsQuickStackAction(request.Action)
                        ? "status.nothing-to-stack" : request.Action == "withdraw"
                            ? "status.storage-updated" : "status.no-space");
                phaseStart = stopwatch.ElapsedMilliseconds;
                updatedCatalog = result.MovedCount > 0 ? RefreshCatalog() : catalog;
                catalogReadMs += stopwatch.ElapsedMilliseconds - phaseStart;
                if (result.MovedCount > 0) MarkStorageMutationSynchronized(updatedCatalog);
            }
        }
        catch (Exception error)
        {
            mod.Monitor.Log($"Remote storage transaction failed: {error}", LogLevel.Error);
            // Each chest plan commits atomically. Preserve already-confirmed
            // entries in a quick-stack batch if a later entry throws.
            if (result.MovedCount == 0) result.Status = mod.T("status.transaction-failed");
            long phaseStart = stopwatch.ElapsedMilliseconds;
            updatedCatalog = RefreshCatalog();
            catalogReadMs += stopwatch.ElapsedMilliseconds - phaseStart;
            if (result.MovedCount > 0) MarkStorageMutationSynchronized(updatedCatalog);
        }
        CacheResult(playerId, result);
        mod.LogInteraction($"host result id={request.RequestId}, player={playerId}, action={request.Action}, " +
            $"moved={result.MovedCount}, status={(result.Status.Length == 0 ? "ok" : "rejected")}");
        mod.SendStorageResult(playerId, result);
        if (updatedCatalog is null)
        {
            long phaseStart = stopwatch.ElapsedMilliseconds;
            updatedCatalog = GetCatalog();
            catalogReadMs += stopwatch.ElapsedMilliseconds - phaseStart;
        }
        long updateStart = stopwatch.ElapsedMilliseconds;
        SendSnapshot(playerId, updatedCatalog);
        if (result.MovedCount > 0)
        {
            mod.RefreshOpenStorageMenu(updatedCatalog);
            BroadcastSnapshotToWatchingPlayers(playerId, updatedCatalog);
            if (IsQuickStackAction(request.Action)) PlayAnimationSafely(player, transfers);
        }
        updateMs = stopwatch.ElapsedMilliseconds - updateStart;
        mod.Monitor.Log($"[performance] remote {request.Action}: total={stopwatch.ElapsedMilliseconds}ms, " +
            $"catalog={catalogReadMs}ms, transfer={operationMs}ms, refresh+broadcast={updateMs}ms, " +
            $"chests={updatedCatalog.Chests.Count}, groups={updatedCatalog.Groups.Count}, " +
            $"entries={request.Items.Count}, moved={result.MovedCount}", LogLevel.Debug);
    }

    internal void ApplyRemoteResult(StorageResult result)
    {
        if (appliedRequestIds.Contains(result.RequestId)) return;
        if (pending is null || pending.RequestId != result.RequestId)
        {
            if (result.Action == "snapshot") mod.ActiveStorageModel?.SetStatus(result.Status);
            return;
        }
        PendingTransfer operation = pending;
        if (operation.AwaitingSnapshot)
        {
            pending = null;
            operation.Model?.SetStatus(result.Status);
            return;
        }
        bool overflow = ApplyResultInventory(operation, result);
        RememberApplied(result.RequestId);
        pending = null;
        if (result.MovedCount > 0)
        {
            string sound = result.Action switch
            {
                "withdraw" => "coin",
                "quick-stack" or "quick-stack-nearby" => "Ship",
                _ => "dwop"
            };
            Game1.playSound(sound);
        }
        StorageMenuModel? liveModel = mod.ActiveStorageModel;
        liveModel?.SetStatus(overflow ? mod.T("status.inventory-overflow") : result.Status);
        liveModel?.Refresh();
    }

    internal void ApplySnapshotReply(StorageSnapshotReply reply, StorageMenuModel? model)
    {
        ApplySnapshot(reply.Snapshot, model);
        if (pending is { AwaitingSnapshot: true } waiting && waiting.RequestId == reply.RequestId)
        {
            pending = null;
            if (reply.Status.Length > 0) waiting.Model?.SetStatus(reply.Status);
            else QuickStack(waiting.Model);
        }
    }

    internal void ApplyBroadcastSnapshot(StorageSnapshot snapshot, StorageMenuModel? model) =>
        ApplySnapshot(snapshot, model);

    internal void ApplyCachedSnapshot(StorageMenuModel model)
    {
        if (latestSnapshot is not null) model.ApplySnapshot(latestSnapshot);
    }

    private void ApplySnapshot(StorageSnapshot snapshot, StorageMenuModel? model)
    {
        if (snapshot.Revision < (latestSnapshot?.Revision ?? -1)) return;
        latestSnapshot = snapshot;
        model?.ApplySnapshot(snapshot);
    }

    internal void UpdateRemoteRequest()
    {
        if (chestInventoryChanged)
        {
            chestInventoryChanged = false;
            if (Context.IsMainPlayer && (mod.IsStorageMenuOpen || watchingStorage.Count > 0))
            {
                ChestCatalog catalog = GetCatalog();
                mod.RefreshOpenStorageMenu(catalog);
                BroadcastSnapshotToWatchingPlayers(long.MinValue, catalog);
            }
        }
        if (Context.IsMainPlayer)
            return;
        if (pending is not { } operation) return;
        int elapsed = Game1.ticks - operation.StartTick;
        if (elapsed >= RetryAfterTicks && operation.RetryCount == 0)
        {
            operation.RetryCount++;
            operation.LastSendTick = Game1.ticks;
            mod.LogInteraction($"retrying request id={operation.RequestId}, action=" +
                $"{operation.Request?.Action ?? "snapshot"}");
            if (operation.Request is not null) mod.SendStorageRequest(operation.Request);
            else mod.SendStorageSnapshotRequest(new StorageSnapshotRequest
                { RequestId = operation.RequestId, UseAutoStoreRange = true });
        }
        if (elapsed < WarnAfterTicks || operation.Warned) return;
        operation.Warned = true;
        operation.Model?.SetStatus(mod.T("status.unconfirmed"));
        if (operation.AwaitingSnapshot) pending = null;
    }

    private void BeginRemoteRequest(StorageRequest request, StorageMenuModel? model,
        List<PendingItem> localItems)
    {
        pending = new PendingTransfer(request.RequestId, request, model, localItems, Game1.ticks);
        mod.LogInteraction($"client sent request id={request.RequestId}, action={request.Action}, " +
            $"key={request.ItemKey}, entries={request.Items.Count}, count={request.Count}");
        mod.SendStorageRequest(request);
    }

    private void SubmitRemoteQuickStack(StorageMenuModel? model, bool useNearbyChests = false)
    {
        var request = new StorageRequest
            { RequestId = Guid.NewGuid().ToString("N"), Action = useNearbyChests ? "quick-stack-nearby" : "quick-stack" };
        var localItems = new List<PendingItem>();
        var deductions = new List<(int Slot, Item Before, Item? After)>();
        try
        {
            for (int slot = 0; slot < Game1.player.Items.Count; slot++)
            {
                Item? item = Game1.player.Items[slot];
                // Include all eligible local stacks. The host's current chest
                // catalog is authoritative, so a stale client snapshot can't
                // hide a newly-added item type from K quick-stack.
                if (item is null || !IsQuickStackItem(item) || ItemLock.IsLocked(item)) continue;
                Item transfer = CopyWithStack(item, item.Stack);
                request.Items.Add(new StorageRequestItem
                    { ItemData = ItemNetCodec.Encode(transfer), Count = transfer.Stack });
                localItems.Add(new PendingItem(transfer, transfer.Stack));
                deductions.Add((slot, item, null));
            }
        }
        catch (Exception error)
        {
            mod.Monitor.Log($"Couldn't prepare quick-stack request: {error}", LogLevel.Warn);
            model?.SetStatus(mod.T("status.transaction-failed"));
            return;
        }
        if (request.Items.Count == 0)
        {
            model?.SetStatus(mod.T("status.nothing-to-stack"));
            return;
        }
        foreach (var deduction in deductions)
            SetBackpackSlot(deduction.Slot, deduction.After);
        model?.SetStatus("");
        model?.Refresh();
        BeginRemoteRequest(request, model, localItems);
    }

    private List<StorageResultItem> RemoteWithdraw(long playerId, ChestCatalog catalog,
        StorageRequest request, out int moved)
    {
        moved = 0;
        if (request.Count <= 0 || request.Count > 999 ||
            !remoteItems.TryGetValue(playerId, out var items) ||
            !items.TryGetValue(request.ItemKey, out Item? exemplar)) return new();
        var sources = catalog.FindGroups(exemplar)
            .SelectMany(group => group.Sources).Where(source => CanUse(source.Chest)).ToList();
        var planned = new List<(ItemSource Source, int Count)>();
        int remaining = request.Count;
        foreach (ItemSource source in sources)
        {
            if (remaining <= 0) break;
            int take = Math.Min(remaining, source.Item.Stack);
            planned.Add((source, take));
            remaining -= take;
        }
        moved = request.Count - remaining;
        if (moved <= 0) return new();
        // Serialize the exact objects before committing the chest removal, so a
        // codec failure cannot consume an item without a recoverable reply.
        var itemsToReturn = planned.Select(entry => new StorageResultItem
        {
            ItemData = ItemNetCodec.Encode(CopyWithStack(entry.Source.Item, entry.Count)),
            Count = entry.Count
        }).ToList();
        var plan = new InventoryPlan<Item>();
        foreach (var entry in planned)
            plan.Set(entry.Source.Chest.Chest.Items, entry.Source.Slot,
                entry.Source.Item.Stack == entry.Count ? null :
                    CopyWithStack(entry.Source.Item, entry.Source.Item.Stack - entry.Count));
        plan.Commit();
        return itemsToReturn;
    }

    private List<int> RemoteDeposit(ChestCatalog catalog, StorageRequest request,
        List<(ChestSource Chest, Item Item)> transfers)
    {
        var accepted = new List<int>();
        if (request.Items.Count != 1) return new() { 0 };
        foreach (StorageRequestItem entry in request.Items)
        {
            if (entry.Count <= 0 || entry.Count > 999 || request.Count != entry.Count)
            { accepted.Add(0); continue; }
            Item item = ItemNetCodec.Decode(entry.ItemData);
            if (item.Stack != entry.Count) { accepted.Add(0); continue; }
            var reached = new List<ChestSource>();
            int moved = StoreItem(catalog, item, entry.Count,
                catalog.Chests.Where(CanUse).ToList(), reached);
            accepted.Add(moved);
            if (moved > 0)
                foreach (ChestSource chest in reached) transfers.Add((chest, item));
        }
        return accepted;
    }

    private List<int> RemoteQuickStack(ChestCatalog catalog, StorageRequest request,
        List<(ChestSource Chest, Item Item)> transfers, HashSet<StardewValley.Objects.Chest>? allowedChests = null)
    {
        var accepted = new List<int>();
        foreach (StorageRequestItem entry in request.Items.Take(128))
        {
            try
            {
                if (entry.Count <= 0 || entry.Count > 999) { accepted.Add(0); continue; }
                Item item = ItemNetCodec.Decode(entry.ItemData);
                if (item.Stack != entry.Count || !IsQuickStackItem(item)) { accepted.Add(0); continue; }
                var matching = catalog.OrderChests(catalog.FindGroups(item)
                    .SelectMany(group => group.Sources.Select(source => source.Chest))
                    .Where(source => CanUse(source) &&
                        (allowedChests is null || allowedChests.Contains(source.Chest)))).ToList();
                var reached = new List<ChestSource>();
                int moved = matching.Count == 0 ? 0 : StoreItem(catalog, item,
                    entry.Count, matching, reached);
                accepted.Add(moved);
                if (moved > 0)
                    foreach (ChestSource chest in reached)
                    {
                        if (transfers.Count >= QuickStackFlightAnimation.MaxAnimatedTransfers) break;
                        try { transfers.Add((chest, item)); }
                        catch (Exception error)
                        { mod.Monitor.Log($"Couldn't queue quick-stack animation: {error}", LogLevel.Trace); }
                    }
            }
            catch (Exception error)
            {
                // A previous entry may already be committed. Keep its accepted
                // count and treat only this independently planned entry as failed.
                mod.Monitor.Log($"Quick-stack entry was rejected: {error}", LogLevel.Warn);
                accepted.Add(0);
            }
        }
        while (accepted.Count < request.Items.Count) accepted.Add(0);
        return accepted;
    }

    private StorageSnapshot BuildSnapshot(long playerId, ChestCatalog catalog)
    {
        var snapshot = new StorageSnapshot
        {
            Revision = ++snapshotRevision,
            ChestCount = catalog.Chests.Count,
            OccupiedSlots = catalog.OccupiedSlots,
            TotalSlots = catalog.TotalSlots
        };
        var keyMap = new Dictionary<string, Item>();
        remoteItems.TryGetValue(playerId, out Dictionary<string, Item>? previousKeys);
        Dictionary<(string Id, int Quality), List<KeyValuePair<string, Item>>>? previousByItem = null;
        if (previousKeys is not null)
            previousByItem = previousKeys
                .GroupBy(entry => (entry.Value.QualifiedItemId, entry.Value.Quality))
                .ToDictionary(group => group.Key, group => group.ToList());
        foreach (ItemGroup group in catalog.Groups)
        {
            string key = Guid.NewGuid().ToString("N");
            if (previousByItem is not null && previousByItem.TryGetValue(
                (group.Exemplar.QualifiedItemId, group.Exemplar.Quality),
                out List<KeyValuePair<string, Item>>? candidates))
            {
                KeyValuePair<string, Item> previous = candidates.FirstOrDefault(entry =>
                    ChestCatalog.SameStack(entry.Value, group.Exemplar));
                if (!string.IsNullOrEmpty(previous.Key)) key = previous.Key;
            }
            keyMap[key] = group.Exemplar;
            snapshot.Items.Add(new StorageSnapshotItem
            {
                Key = key,
                QualifiedItemId = group.Exemplar.QualifiedItemId,
                Quality = group.Exemplar.Quality,
                Count = group.Total,
                PreservedItemId = (group.Exemplar as SObject)?.preservedParentSheetIndex.Value
            });
        }
        remoteItems[playerId] = keyMap;
        return snapshot;
    }

    private void SendSnapshotReply(long playerId, string requestId, StorageSnapshot snapshot) =>
        mod.SendStorageSnapshotReply(playerId, new StorageSnapshotReply
            { RequestId = requestId, Snapshot = snapshot });

    private void SendSnapshot(long playerId, ChestCatalog catalog) =>
        mod.SendStorageSnapshot(playerId, BuildSnapshot(playerId, catalog));

    private void SendSnapshot(long playerId, StorageSnapshot snapshot) =>
        mod.SendStorageSnapshot(playerId, snapshot);

    private void BroadcastSnapshotToWatchingPlayers(long actorId, ChestCatalog catalog)
    {
        foreach (long playerId in watchingStorage.ToArray())
        {
            Farmer? viewer = Game1.getOnlineFarmers()
                .FirstOrDefault(farmer => farmer.UniqueMultiplayerID == playerId);
            if (playerId == actorId || viewer is null) continue;
            try { SendSnapshot(playerId, BuildSnapshot(playerId, catalog)); }
            catch (Exception error)
            {
                mod.Monitor.Log($"Could not refresh storage for player " +
                    $"{playerId}: {error}", LogLevel.Warn);
            }
        }
    }

    internal void RequestClientSnapshot(bool subscribe)
    {
        if (Context.IsMainPlayer) return;
        string id = Guid.NewGuid().ToString("N");
        mod.SendStorageSnapshotRequest(new StorageSnapshotRequest
            { RequestId = id, Subscribe = subscribe });
        if (subscribe) subscribedToStorage = true;
    }

    private bool ApplyResultInventory(PendingTransfer operation, StorageResult result)
    {
        bool overflow = false;
        if (result.Action is "deposit" or "quick-stack" or "quick-stack-nearby")
        {
            for (int i = 0; i < operation.LocalItems.Count; i++)
            {
                PendingItem local = operation.LocalItems[i];
                int accepted = i < result.AcceptedCounts.Count
                    ? Math.Clamp(result.AcceptedCounts[i], 0, local.Count) : 0;
                int refund = local.Count - accepted;
                if (refund > 0) overflow |= ReturnToInventory(CopyWithStack(local.Item, refund));
            }
        }
        else if (result.Action == "withdraw")
        {
            foreach (StorageResultItem entry in result.Items)
            {
                if (entry.Count <= 0 || entry.Count > 999) continue;
                Item item = ItemNetCodec.Decode(entry.ItemData);
                if (item.Stack != entry.Count) item.Stack = entry.Count;
                overflow |= ReturnToInventory(item);
            }
        }
        return overflow;
    }

    private static bool ReturnToInventory(Item item)
    {
        Item? remaining = Game1.player.addItemToInventory(item);
        if (remaining is null) return false;
        Game1.createItemDebris(remaining, Game1.player.Position, Game1.player.FacingDirection);
        return true;
    }

    private static void SetBackpackSlot(int index, Item? item)
    {
        while (Game1.player.Items.Count <= index) Game1.player.Items.Add(null);
        Game1.player.Items[index] = item;
    }

    private void RememberApplied(string requestId)
    {
        if (!appliedRequestIds.Add(requestId)) return;
        appliedRequestOrder.Enqueue(requestId);
        while (appliedRequestOrder.Count > 256)
            appliedRequestIds.Remove(appliedRequestOrder.Dequeue());
    }

    private bool TryGetCachedResult(long playerId, string requestId, out StorageResult result)
    {
        if (hostResults.TryGetValue(playerId, out RequestResultCache? cache) &&
            cache.Results.TryGetValue(requestId, out result!)) return true;
        result = null!;
        return false;
    }

    private void CacheResult(long playerId, StorageResult result)
    {
        if (!hostResults.TryGetValue(playerId, out RequestResultCache? cache))
            hostResults[playerId] = cache = new RequestResultCache();
        cache.Results[result.RequestId] = result;
        cache.Order.Enqueue(result.RequestId);
        while (cache.Order.Count > MaxCachedResults)
            cache.Results.Remove(cache.Order.Dequeue());
    }

    private sealed class RequestResultCache
    {
        internal Dictionary<string, StorageResult> Results { get; } = new();
        internal Queue<string> Order { get; } = new();
    }

    private sealed class PendingTransfer(string requestId, StorageRequest? request,
        StorageMenuModel? model, List<PendingItem> localItems, int tick, bool awaitingSnapshot = false)
    {
        internal string RequestId { get; } = requestId;
        internal StorageRequest? Request { get; } = request;
        internal StorageMenuModel? Model { get; } = model;
        internal List<PendingItem> LocalItems { get; } = localItems;
        internal int StartTick { get; } = tick;
        internal int LastSendTick { get; set; } = tick;
        internal int RetryCount { get; set; }
        internal bool Warned { get; set; }
        internal bool AwaitingSnapshot { get; } = awaitingSnapshot;
        internal static PendingTransfer WaitingForSnapshot(string id, StorageMenuModel? model) =>
            new(id, null, model, new(), Game1.ticks, awaitingSnapshot: true);
    }

    private sealed record PendingItem(Item Item, int Count);

    private static int QuickStack(ChestCatalog catalog, Farmer player,
        List<(ChestSource Chest, Item Item)> transfers, HashSet<int>? excludedSlots = null,
        HashSet<StardewValley.Objects.Chest>? allowedChests = null)
    {
        var eligible = catalog.Groups.Where(group => IsQuickStackItem(group.Exemplar))
            .ToLookup(group => group.Exemplar.QualifiedItemId);
        int total = 0;
        for (int slot = 0; slot < player.Items.Count; slot++)
        {
            Item? item = player.Items[slot];
            if (item is null || !IsQuickStackItem(item) || ItemLock.IsLocked(item) ||
                excludedSlots?.Contains(slot) == true) continue;
            var matching = catalog.OrderChests(eligible[item.QualifiedItemId]
                .Where(entry => ChestCatalog.SameStack(entry.Exemplar, item))
                .SelectMany(entry => entry.Sources.Select(source => source.Chest))
                .Where(source => CanUse(source) &&
                    (allowedChests is null || allowedChests.Contains(source.Chest)))).ToList();
            if (matching.Count == 0) continue;
            var reached = new List<ChestSource>();
            int moved = Deposit(catalog, player, slot, item, item.Stack, matching, reached);
            if (moved > 0)
            {
                total += moved;
                int animationSlots = QuickStackFlightAnimation.MaxAnimatedTransfers - transfers.Count;
                if (animationSlots > 0)
                    transfers.AddRange(reached.Take(animationSlots).Select(chest => (chest, item)));
            }
        }
        return total;
    }

    private static int StoreItem(ChestCatalog catalog, Item item, int requested,
        IReadOnlyList<ChestSource> destinations, List<ChestSource>? reachedChests = null)
    {
        if (requested <= 0 || destinations.Count == 0) return 0;
        int amount = Math.Min(requested, item.Stack);
        if (amount <= 0) return 0;
        var plan = new InventoryPlan<Item>();
        var reached = new HashSet<ChestSource>();
        if (item.maximumStackSize() <= 1)
        {
            foreach (ChestSource chest in destinations)
            {
                int empty = FirstEmpty(plan, chest.Chest.Items, chest.Chest.GetActualCapacity());
                if (empty < 0) continue;
                plan.Set(chest.Chest.Items, empty, item);
                plan.Commit();
                reachedChests?.Add(chest);
                return 1;
            }
            return 0;
        }

        int remaining = amount;
        foreach (ChestSource source in destinations)
        {
            for (int i = 0; i < source.Chest.Items.Count && remaining > 0; i++)
            {
                Item? target = plan.Get(source.Chest.Items, i);
                if (target is null || !ChestCatalog.SameStack(target, item)) continue;
                int fill = Math.Min(remaining,
                    Math.Max(0, target.maximumStackSize() - target.Stack));
                if (fill <= 0) continue;
                plan.Set(source.Chest.Items, i, CopyWithStack(target, target.Stack + fill));
                reached.Add(source);
                remaining -= fill;
            }
        }
        foreach (ChestSource source in destinations)
        {
            int capacitySlots = source.Chest.GetActualCapacity();
            for (int i = 0; i < capacitySlots && remaining > 0; i++)
            {
                if (plan.Get(source.Chest.Items, i) is not null) continue;
                int fill = Math.Min(remaining, item.maximumStackSize());
                plan.Set(source.Chest.Items, i, CopyWithStack(item, fill));
                reached.Add(source);
                remaining -= fill;
            }
        }
        amount -= remaining;
        if (amount <= 0) return 0;
        plan.Commit();
        reachedChests?.AddRange(reached);
        return amount;
    }

    private void PlayAnimationSafely(Farmer player, List<(ChestSource Chest, Item Item)> transfers)
    {
        try
        {
            StoragePulse pulse = QuickStackFlightAnimation.Play(player, transfers);
            mod.BroadcastStoragePulse(pulse);
        }
        catch (Exception error)
        {
            mod.Monitor.Log($"Quick-stack animation failed after items were stored: {error}", LogLevel.Warn);
        }
    }

    private void Execute(StorageMenuModel? model, Func<ChestCatalog, string> operation,
        bool useAutoStoreRange = false, bool useNearbyChestRange = false)
    {
        bool inRange = useNearbyChestRange ? mod.IsNearSupportedChest() :
            useAutoStoreRange ? mod.IsInAutoStoreRange() : mod.IsNearTerminal();
        if (!inRange)
        {
            mod.LogInteraction("transfer blocked: terminal out of range");
            model?.SetStatus(mod.T("status.too-far"));
            return;
        }
        mod.LogInteraction($"transaction mode: multiplayer={Context.IsMultiplayer}, " +
            $"mainPlayer={Context.IsMainPlayer}, onlinePlayers={Game1.getOnlineFarmers().Count()}, direct=true");
        // Host transactions run on the game thread. They check the game's chest
        // mutexes before writing and complete without yielding to network events.
        // RequestLock callbacks depend on location updates and can wait forever
        // for a chest in an unloaded location.
        ChestCatalog catalog = GetCatalog();
        try
        {
            string status = operation(catalog);
            model?.SetStatus(status);
            if (status.Length == 0 && Context.IsMultiplayer)
            {
                catalog = RefreshCatalog();
                MarkStorageMutationSynchronized(catalog);
                BroadcastSnapshotToWatchingPlayers(Game1.player.UniqueMultiplayerID, catalog);
            }
            else if (status.Length == 0)
            {
                catalog = RefreshCatalog();
                MarkStorageMutationSynchronized(catalog);
            }
        }
        catch (Exception error)
        {
            mod.Monitor.Log($"Storage transaction failed: {error}", LogLevel.Error);
            model?.SetStatus(mod.T("status.transaction-failed"));
            catalog = RefreshCatalog();
        }
        finally { model?.RefreshFromCatalog(catalog); }
    }

    private static bool CanUse(ChestSource source)
    {
        return !source.Chest.GetMutex().IsLocked();
    }

    private static bool IsQuickStackItem(Item item) =>
        item is SObject obj && !obj.bigCraftable.Value && item.maximumStackSize() > 1;

    private static bool IsQuickStackAction(string action) =>
        action is "quick-stack" or "quick-stack-nearby";

    private int Withdraw(ChestCatalog catalog, Farmer player, Item exemplar, int requested)
    {
        var sources = catalog.FindGroups(exemplar)
            .SelectMany(group => group.Sources).Where(source => CanUse(source.Chest)).ToList();
        int available = sources.Sum(source => source.Item.Stack);
        int capacity = Capacity(player.Items, player.MaxItems, exemplar);
        int amount = Math.Min(requested, Math.Min(available, capacity));
        mod.LogInteraction($"withdraw plan: item={exemplar.QualifiedItemId}, " +
            $"sources={sources.Count}, available={available}, backpackCapacity={capacity}, " +
            $"requested={requested}, planned={amount}");
        if (amount <= 0) return 0;
        var plan = new InventoryPlan<Item>();
        if (exemplar.maximumStackSize() <= 1)
        {
            ItemSource source = sources.First();
            int empty = FirstEmpty(plan, player.Items, player.MaxItems);
            if (empty < 0) return 0;
            plan.Set(player.Items, empty, source.Item);
            plan.Set(source.Chest.Chest.Items, source.Slot, null);
            plan.Commit();
            return 1;
        }
        int inserted = PlanAdd(plan, player.Items, player.MaxItems, exemplar, amount);
        if (inserted != amount) return 0;
        int remaining = amount;
        foreach (ItemSource source in sources)
        {
            if (remaining == 0) break;
            int take = Math.Min(remaining, source.Item.Stack);
            Item? copy = source.Item.Stack == take ? null : CopyWithStack(source.Item, source.Item.Stack - take);
            plan.Set(source.Chest.Chest.Items, source.Slot, copy);
            remaining -= take;
        }
        if (remaining != 0) return 0;
        plan.Commit();
        return amount;
    }

    private static int Deposit(ChestCatalog catalog, Farmer player, int backpackIndex, Item exemplar, int requested,
        IReadOnlyList<ChestSource>? destinations = null, List<ChestSource>? reachedChests = null)
    {
        if (backpackIndex < 0 || backpackIndex >= player.Items.Count) return 0;
        Item? bagItem = player.Items[backpackIndex];
        if (bagItem is null || !ChestCatalog.SameStack(bagItem, exemplar)) return 0;
        destinations ??= catalog.Chests;
        int capacity = destinations.Sum(chest => Capacity(chest.Chest.Items, chest.Chest.GetActualCapacity(), bagItem));
        int amount = Math.Min(requested, Math.Min(bagItem.Stack, capacity));
        if (amount <= 0) return 0;
        var plan = new InventoryPlan<Item>();
        if (bagItem.maximumStackSize() <= 1)
        {
            foreach (ChestSource chest in destinations)
            {
                int empty = FirstEmpty(plan, chest.Chest.Items, chest.Chest.GetActualCapacity());
                if (empty < 0) continue;
                plan.Set(chest.Chest.Items, empty, bagItem);
                plan.Set(player.Items, backpackIndex, null);
                plan.Commit();
                return 1;
            }
            return 0;
        }
        int remaining = amount;
        var reached = new HashSet<ChestSource>();
        // Every existing matching stack is filled before any empty slot.
        foreach (ChestSource source in destinations)
        {
            for (int i = 0; i < source.Chest.Items.Count && remaining > 0; i++)
            {
                Item? target = plan.Get(source.Chest.Items, i);
                if (target is null || !ChestCatalog.SameStack(target, bagItem)) continue;
                int fill = Math.Min(remaining, Math.Max(0, target.maximumStackSize() - target.Stack));
                if (fill == 0) continue;
                plan.Set(source.Chest.Items, i, CopyWithStack(target, target.Stack + fill));
                reached.Add(source);
                remaining -= fill;
            }
        }
        foreach (ChestSource source in destinations)
        {
            int limit = source.Chest.GetActualCapacity();
            for (int i = 0; i < limit && remaining > 0; i++)
            {
                if (plan.Get(source.Chest.Items, i) is not null) continue;
                int fill = Math.Min(remaining, bagItem.maximumStackSize());
                plan.Set(source.Chest.Items, i, CopyWithStack(bagItem, fill));
                reached.Add(source);
                remaining -= fill;
            }
        }
        if (remaining != 0) throw new InvalidOperationException("Chest capacity changed after validation.");
        plan.Set(player.Items, backpackIndex,
            bagItem.Stack == amount ? null : CopyWithStack(bagItem, bagItem.Stack - amount));
        plan.Commit();
        if (reachedChests is not null) reachedChests.AddRange(reached);
        return amount;
    }

    private static Item CopyWithStack(Item item, int stack)
    {
        Item copy = item.getOne();
        copy.Stack = stack;
        return copy;
    }

    private static int FirstEmpty(InventoryPlan<Item> plan, IList<Item?> items, int slots)
    {
        for (int i = 0; i < slots; i++) if (plan.Get(items, i) is null) return i;
        return -1;
    }

    private static int Capacity(IList<Item?> items, int slots, Item exemplar)
    {
        int total = 0;
        for (int i = 0; i < slots; i++)
        {
            Item? target = i < items.Count ? items[i] : null;
            if (target is null) total += exemplar.maximumStackSize();
            else if (ChestCatalog.SameStack(target, exemplar))
                total += Math.Max(0, target.maximumStackSize() - target.Stack);
        }
        return total;
    }

    private static int PlanAdd(InventoryPlan<Item> plan, IList<Item?> items, int slots, Item exemplar, int amount)
    {
        int remaining = amount;
        for (int i = 0; i < items.Count && remaining > 0; i++)
        {
            Item? target = plan.Get(items, i);
            if (target is null || !ChestCatalog.SameStack(target, exemplar)) continue;
            int fill = Math.Min(remaining, Math.Max(0, target.maximumStackSize() - target.Stack));
            if (fill == 0) continue;
            plan.Set(items, i, CopyWithStack(target, target.Stack + fill));
            remaining -= fill;
        }
        for (int i = 0; i < slots && remaining > 0; i++)
        {
            if (plan.Get(items, i) is not null) continue;
            int fill = Math.Min(remaining, exemplar.maximumStackSize());
            plan.Set(items, i, CopyWithStack(exemplar, fill));
            remaining -= fill;
        }
        return amount - remaining;
    }

}
