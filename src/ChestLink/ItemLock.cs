using StardewValley;

namespace ChestLink;

/// <summary>Item-level marker; manual storage interactions deliberately ignore it.</summary>
internal static class ItemLock
{
    private const string Key = "Nooobad.ChestLink/Locked";

    internal static bool IsLocked(Item item) =>
        item.modData.TryGetValue(Key, out string? value) && value == "true";

    internal static bool Toggle(Item item)
    {
        if (IsLocked(item))
        {
            item.modData.Remove(Key);
            return false;
        }
        item.modData[Key] = "true";
        return true;
    }
}
