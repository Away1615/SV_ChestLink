namespace ChestLink;

/// <summary>Stage inventory changes, then apply or roll them back as one operation.</summary>
internal sealed class InventoryPlan<T> where T : class
{
    private readonly List<(IList<T?> Items, int Slot, T? Before, T? After)> changes = new();
    private readonly Dictionary<IList<T?>, Dictionary<int, int>> changeIndexes =
        new(ReferenceEqualityComparer.Instance);

    internal T? Get(IList<T?> items, int slot)
    {
        if (changeIndexes.TryGetValue(items, out Dictionary<int, int>? slots) &&
            slots.TryGetValue(slot, out int index))
            return changes[index].After;
        return slot < items.Count ? items[slot] : null;
    }

    internal void Set(IList<T?> items, int slot, T? after)
    {
        if (!changeIndexes.TryGetValue(items, out Dictionary<int, int>? slots))
        {
            slots = new Dictionary<int, int>();
            changeIndexes.Add(items, slots);
        }
        if (slots.TryGetValue(slot, out int index))
        {
            var change = changes[index];
            changes[index] = (items, slot, change.Before, after);
            return;
        }
        slots.Add(slot, changes.Count);
        changes.Add((items, slot, slot < items.Count ? items[slot] : null, after));
    }

    internal void Commit()
    {
        var applied = new List<(IList<T?> Items, int Slot, T? Before, bool Added)>();
        try
        {
            foreach (var change in changes)
            {
                bool added = change.Slot >= change.Items.Count;
                if (added)
                {
                    if (change.Slot != change.Items.Count)
                        throw new InvalidOperationException("Inventory plan has a gap.");
                    change.Items.Add(change.After);
                }
                else change.Items[change.Slot] = change.After;
                applied.Add((change.Items, change.Slot, change.Before, added));
            }
        }
        catch
        {
            foreach (var change in applied.AsEnumerable().Reverse())
            {
                if (change.Added) change.Items.RemoveAt(change.Slot);
                else change.Items[change.Slot] = change.Before;
            }
            throw;
        }
    }
}
