namespace ChestLink;

/// <summary>A client's intent; item payloads are full NetFields copies.</summary>
internal sealed class StorageRequest
{
    public string RequestId { get; set; } = "";
    public string Action { get; set; } = "";
    public string ItemKey { get; set; } = "";
    public int Count { get; set; }
    public List<StorageRequestItem> Items { get; set; } = new();
}

internal sealed class StorageRequestItem
{
    public string ItemData { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>The immutable result of one request, replayable for duplicate request IDs.</summary>
internal sealed class StorageResult
{
    public string RequestId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Status { get; set; } = "";
    public int MovedCount { get; set; }
    public List<int> AcceptedCounts { get; set; } = new();
    public List<StorageResultItem> Items { get; set; } = new();
}

internal sealed class StorageResultItem
{
    public string ItemData { get; set; } = "";
    public int Count { get; set; }
}

internal sealed class StorageSnapshot
{
    public long Revision { get; set; }
    public int ChestCount { get; set; }
    public int OccupiedSlots { get; set; }
    public int TotalSlots { get; set; }
    public List<StorageSnapshotItem> Items { get; set; } = new();
}

internal sealed class StorageSnapshotReply
{
    public string RequestId { get; set; } = "";
    public string Status { get; set; } = "";
    public StorageSnapshot Snapshot { get; set; } = new();
}

internal sealed class StorageSnapshotRequest
{
    public string RequestId { get; set; } = "";
    public bool UseAutoStoreRange { get; set; }
    public bool Subscribe { get; set; }
}

internal sealed class StorageSnapshotItem
{
    public string Key { get; set; } = "";
    public string QualifiedItemId { get; set; } = "";
    public int Quality { get; set; }
    public int Count { get; set; }
    public string? PreservedItemId { get; set; }
}

internal sealed class StoragePulse
{
    public List<StoragePulseChest> Chests { get; set; } = new();
}

internal sealed class StoragePulseChest
{
    public string LocationName { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int OpenDelayMs { get; set; }
    public int CloseDelayMs { get; set; }
}
