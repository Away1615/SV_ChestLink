using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace ChestLink;

internal sealed record ChestSource(GameLocation Location, Vector2 Tile, Chest Chest);

internal sealed record ItemSource(ChestSource Chest, int Slot, Item Item);

internal sealed class ItemGroup(Item exemplar)
{
    internal Item Exemplar { get; } = exemplar;
    internal List<ItemSource> Sources { get; } = new();
    internal int Total { get; private set; }

    internal void AddSource(ItemSource source)
    {
        Sources.Add(source);
        Total += source.Item.Stack;
    }
}

internal sealed class ChestCatalog
{
    internal IReadOnlyList<ChestSource> Chests { get; }
    internal IReadOnlyList<ItemGroup> Groups { get; }
    internal int LocationCount { get; }
    internal int OccupiedSlots { get; }
    internal int TotalSlots { get; }
    private readonly Dictionary<(string Id, int Quality), List<ItemGroup>> groupsByItem;
    private readonly Dictionary<ChestSource, int> chestOrder;

    private ChestCatalog(int locationCount, List<ChestSource> chests, List<ItemGroup> groups)
    {
        Chests = chests;
        Groups = groups;
        LocationCount = locationCount;
        OccupiedSlots = chests.Sum(source => source.Chest.Items.Count(item => item is { Stack: > 0 }));
        TotalSlots = chests.Sum(source => source.Chest.GetActualCapacity());
        chestOrder = chests.Select((source, index) => (source, index))
            .ToDictionary(entry => entry.source, entry => entry.index);
        groupsByItem = groups.GroupBy(group =>
                (group.Exemplar.QualifiedItemId, group.Exemplar.Quality))
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    internal static ChestCatalog Read()
    {
        var locations = new List<GameLocation>();
        void AddLocation(GameLocation location)
        {
            if (!locations.Any(existing => ReferenceEquals(existing, location)))
                locations.Add(location);
        }

        Utility.ForEachLocation(location =>
        {
            AddLocation(location);
            return true;
        }, includeInteriors: true, includeGenerated: true);
        if (Game1.currentLocation is { } currentLocation)
            AddLocation(currentLocation);
        locations.Sort((a, b) => StringComparer.Ordinal.Compare(a.NameOrUniqueName, b.NameOrUniqueName));

        var chests = new List<ChestSource>();
        foreach (GameLocation location in locations)
        {
            foreach (var pair in location.Objects.Pairs)
            {
                if (pair.Value is Chest chest && IsOrdinary(chest))
                    chests.Add(new ChestSource(location, pair.Key, chest));
            }
        }
        chests.Sort((a, b) =>
        {
            int value = StringComparer.Ordinal.Compare(a.Location.NameOrUniqueName,
                b.Location.NameOrUniqueName);
            if (value != 0) return value;
            value = a.Tile.Y.CompareTo(b.Tile.Y);
            return value != 0 ? value : a.Tile.X.CompareTo(b.Tile.X);
        });

        var groups = new List<ItemGroup>();
        var groupsByItem = new Dictionary<(string Id, int Quality), List<ItemGroup>>();
        foreach (ChestSource source in chests)
        {
            for (int slot = 0; slot < source.Chest.Items.Count; slot++)
            {
                Item? item = source.Chest.Items[slot];
                if (item is null || item.Stack <= 0)
                    continue;
                var key = (item.QualifiedItemId, item.Quality);
                if (!groupsByItem.TryGetValue(key, out List<ItemGroup>? candidates))
                {
                    candidates = new List<ItemGroup>();
                    groupsByItem.Add(key, candidates);
                }
                ItemGroup? group = candidates.FirstOrDefault(candidate => SameStack(candidate.Exemplar, item));
                if (group is null)
                {
                    group = new ItemGroup(item);
                    groups.Add(group);
                    candidates.Add(group);
                }
                group.AddSource(new ItemSource(source, slot, item));
            }
        }
        return new ChestCatalog(locations.Count, chests, groups);
    }

    internal IEnumerable<ItemGroup> FindGroups(Item item) =>
        groupsByItem.TryGetValue((item.QualifiedItemId, item.Quality),
            out List<ItemGroup>? candidates)
            ? candidates.Where(group => SameStack(group.Exemplar, item))
            : Enumerable.Empty<ItemGroup>();

    internal IEnumerable<ChestSource> OrderChests(IEnumerable<ChestSource> sources) =>
        sources.Distinct().OrderBy(source => chestOrder[source]);

    internal static bool IsOrdinary(Chest chest)
    {
        if (chest.SpecialChestType != Chest.SpecialChestTypes.None || !chest.playerChest.Value ||
            chest.giftbox.Value || !string.IsNullOrEmpty(chest.GlobalInventoryId))
            return false;
        return chest.QualifiedItemId is "(BC)130" or "(BC)232" or "(BC)BigChest" or "(BC)BigStoneChest";
    }

    internal static bool SameStack(Item left, Item right) =>
        ReferenceEquals(left, right) ||
        (left.QualifiedItemId == right.QualifiedItemId && left.Quality == right.Quality &&
         left.canStackWith(right) && right.canStackWith(left));
}
