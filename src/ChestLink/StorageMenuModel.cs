using System.ComponentModel;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using SObject = StardewValley.Object;

namespace ChestLink;

public sealed class StorageMenuModel : INotifyPropertyChanged
{
    private const int PageSize = 60;
    private readonly ModEntry mod;
    private ChestCatalog? catalog;
    private List<(Item Exemplar, int Count, string Key, StorageContainerKind Kind)> remoteGroups = new();
    private List<StorageContainerStats> remoteContainers = new();
    private int page;
    private string category = "farming";
    private StorageContainerKind containerKind = StorageContainerKind.All;
    private string searchText = "";
    internal string SelectedCategory => category;
    internal StorageContainerKind SelectedContainerKind => containerKind;

    public event PropertyChangedEventHandler? PropertyChanged;
    public List<CategoryTab> Categories { get; }
    internal List<ContainerTab> Containers { get; }
    public List<StorageSlot> StorageSlots { get; private set; } = new();
    public List<StorageSlot> BackpackSlots { get; private set; } = new();
    public string SearchText => searchText;
    public int OccupiedChestSlots { get; private set; }
    public int TotalChestSlots { get; private set; }
    public int ChestCount { get; private set; }
    public int PageNumber { get; private set; } = 1;
    public int PageCount { get; private set; } = 1;
    public string Status { get; private set; } = "";
    public bool HasConfigMenu => mod.HasConfigMenu;
    public StorageClickAction BoundSlotAction => mod.BoundSlotAction;
    internal string T(string key, object? tokens = null) => mod.T(key, tokens);
    internal void LogInteraction(string message) => mod.LogInteraction(message);

    public StorageMenuModel(ModEntry mod)
    {
        this.mod = mod;
        Categories = new[]
        {
            ("farming", "(O)262"), ("animal", "(O)176"),
            ("fishing", "(O)129"), ("forage", "(O)296"),
            ("resources", "(O)709"), ("minerals", "(O)72"),
            ("monster", "(O)766"), ("cooking", "(O)194"),
            ("artisan", "(O)340"), ("manufactured", "(BC)8"),
            ("equipment", "(T)Axe"),
            ("clothing", "(H)0"), ("other", "(O)167")
        }.Select(pair => new CategoryTab(this, pair.Item1,
            pair.Item2.Length > 0 ? ItemRegistry.GetData(pair.Item2) : null)).ToList();
        Containers = new[]
        {
            (StorageContainerKind.All, ""),
            (StorageContainerKind.Chest, "(BC)130"),
            (StorageContainerKind.Fridge, "(BC)216")
        }.Select(pair => new ContainerTab(this, pair.Item1,
            pair.Item2.Length > 0 ? ItemRegistry.GetData(pair.Item2) : null)).ToList();
        Refresh();
    }

    internal void SelectContainer(StorageContainerKind kind)
    {
        if (containerKind == kind) return;
        containerKind = kind;
        page = 0;
        foreach (ContainerTab tab in Containers) tab.NotifySelectionChanged();
        Refresh();
    }

    public void SelectCategory(string name)
    {
        category = name;
        searchText = "";
        page = 0;
        foreach (CategoryTab tab in Categories) tab.NotifySelectionChanged();
        Refresh();
    }
    public void SetSearch(string text)
    {
        text = text.Trim();
        if (searchText == text) return;
        searchText = text;
        page = 0;
        Refresh();
    }
    public void PreviousPage() { if (page > 0) { page--; Refresh(); } }
    public void NextPage() { page++; Refresh(); }
    public void QuickStack() => mod.QuickStack(this);
    public bool OpenConfigMenu() => mod.OpenConfigMenu();
    internal void SetStatus(string message)
    {
        Status = message;
        Changed(nameof(Status));
    }

    internal void ApplySnapshot(StorageSnapshot snapshot)
    {
        remoteGroups = new();
        foreach (StorageSnapshotItem entry in snapshot.Items)
        {
            try
            {
                Item item = ItemRegistry.Create(entry.QualifiedItemId);
                item.Quality = entry.Quality;
                if (item is SObject obj && entry.PreservedItemId is not null)
                    obj.preservedParentSheetIndex.Value = entry.PreservedItemId;
                remoteGroups.Add((item, entry.Count, entry.Key, entry.ContainerKind));
            }
            catch (Exception error)
            {
                mod.Monitor.Log($"Cannot display remote storage item {entry.QualifiedItemId}: {error}",
                    StardewModdingAPI.LogLevel.Warn);
            }
        }
        remoteContainers = snapshot.Containers;
        Refresh();
    }

    internal void Refresh()
    {
        ChestCatalog? currentCatalog = catalog;
        if (Context.IsMainPlayer && currentCatalog is null)
            catalog = currentCatalog = mod.GetStorageCatalog();
        if (Context.IsMainPlayer)
        {
            var chests = currentCatalog!.ViewChests(containerKind).ToList();
            ChestCount = chests.Count;
            OccupiedChestSlots = chests.Sum(source =>
                source.Chest.Items.Count(item => item is { Stack: > 0 }));
            TotalChestSlots = chests.Sum(source => source.Chest.GetActualCapacity());
        }
        else
        {
            StorageContainerStats? stats = remoteContainers.FirstOrDefault(entry =>
                entry.Kind == containerKind);
            ChestCount = stats?.Count ?? 0;
            OccupiedChestSlots = stats?.OccupiedSlots ?? 0;
            TotalChestSlots = stats?.TotalSlots ?? 0;
        }
        var groups = Context.IsMainPlayer
            ? currentCatalog!.ViewGroups(containerKind).Select(entry =>
                (entry.Group.Exemplar, entry.Count, Key: ""))
            : remoteGroups.Where(entry => entry.Kind == containerKind)
                .Select(entry => (entry.Exemplar, entry.Count, entry.Key));
        var matching = groups.Where(group => searchText.Length > 0
                ? group.Exemplar.DisplayName.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
                : CategoryOf(group.Exemplar) == category).ToList();
        // Vanilla organizeItemsInList keeps tools in their existing order, then sorts
        // everything else with Item.CompareTo. Sorting catalog groups never mutates chests.
        var filtered = matching.Where(group => group.Exemplar is Tool)
            .Concat(matching.Where(group => group.Exemplar is not Tool)
                .OrderBy(group => group.Exemplar, Comparer<Item>.Default)).ToList();
        var virtualStacks = new List<(Item Item, int Count, string Key)>();
        foreach (var group in filtered)
        {
            int remaining = group.Count;
            int limit = Math.Max(1, Math.Min(999, group.Exemplar.maximumStackSize()));
            while (remaining > 0)
            {
                int count = Math.Min(remaining, limit);
                virtualStacks.Add((group.Exemplar, count, group.Key));
                remaining -= count;
            }
        }
        int pages = Math.Max(1, (virtualStacks.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        PageNumber = page + 1;
        PageCount = pages;
        StorageSlots = virtualStacks.Skip(page * PageSize).Take(PageSize)
            .Select(entry => new StorageSlot(this, entry.Item, entry.Count, true,
                remoteKey: entry.Key, containerKind: containerKind)).ToList();
        while (StorageSlots.Count < PageSize)
            StorageSlots.Add(new StorageSlot(this, null, 0, true, containerKind: containerKind));
        BackpackSlots = new List<StorageSlot>();
        for (int i = 0; i < Game1.player.MaxItems; i++)
        {
            Item? item = i < Game1.player.Items.Count ? Game1.player.Items[i] : null;
            BackpackSlots.Add(new StorageSlot(this, item, item?.Stack ?? 0, false, i,
                containerKind: containerKind));
        }
        Changed(nameof(StorageSlots)); Changed(nameof(BackpackSlots));
        Changed(nameof(OccupiedChestSlots)); Changed(nameof(TotalChestSlots)); Changed(nameof(ChestCount));
    }

    internal void RefreshFromCatalog(ChestCatalog updatedCatalog)
    {
        catalog = updatedCatalog;
        Refresh();
    }

    internal void Transfer(StorageSlot slot, bool single) => Transfer(slot, single ? 1 : slot.Count);

    internal void Transfer(StorageSlot slot, int amount)
    {
        if (slot.Item is null) return;
        mod.Transfer(this, slot, amount);
    }

    private static string CategoryOf(Item item)
    {
        string? objectType = item is SObject &&
            Game1.objectData.TryGetValue(item.ItemId, out var data) ? data.Type : null;
        return StorageCategoryRules.Classify(item.QualifiedItemId, objectType, item.Category);
    }

    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

public sealed class CategoryTab(StorageMenuModel model, string key, ParsedItemData? icon) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Key { get; } = key;
    public string Name => model.T("category." + Key);
    public ParsedItemData? Icon { get; } = icon;
    public bool IsSelected => model.SelectedCategory == Key;
    public bool IsUnselected => !IsSelected;
    public void Select() => model.SelectCategory(Key);
    internal void NotifySelectionChanged()
    {
        PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
        PropertyChanged?.Invoke(this, new(nameof(IsUnselected)));
    }
}

internal sealed class ContainerTab(StorageMenuModel model, StorageContainerKind kind,
    ParsedItemData? icon) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    internal StorageContainerKind Kind { get; } = kind;
    internal string Name => model.T("container." + Kind.ToString().ToLowerInvariant());
    internal ParsedItemData? Icon { get; } = icon;
    internal bool IsSelected => model.SelectedContainerKind == Kind;
    internal void Select() => model.SelectContainer(Kind);
    internal void NotifySelectionChanged() => PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
}

public sealed class StorageSlot(StorageMenuModel model, Item? item, int count, bool inStorage,
    int backpackIndex = -1, string remoteKey = "",
    StorageContainerKind containerKind = StorageContainerKind.All)
{
    public Item? Item { get; } = item;
    public int Count { get; } = count;
    public bool InStorage { get; } = inStorage;
    public int BackpackIndex { get; } = backpackIndex;
    internal string RemoteKey { get; } = remoteKey;
    internal StorageContainerKind ContainerKind { get; } = containerKind;
    public ParsedItemData? Sprite => Item is null ? null : ItemRegistry.GetData(Item.QualifiedItemId);
    public bool HasItem => Item is not null;
    public bool HasSource => SourceSprite is not null;
    public string Tooltip => Item is null ? "" : $"{Item.DisplayName} × {Count}";
    public string Quality => Item is { Quality: > 0 } ? $"Q{Item.Quality}" : "";
    public ParsedItemData? SourceSprite => Item is SObject obj && !string.IsNullOrEmpty(obj.preservedParentSheetIndex.Value)
        ? ItemRegistry.GetData("(O)" + obj.preservedParentSheetIndex.Value) : null;
    public void LeftClick() => model.Transfer(this, false);
    public void RightClick() => model.Transfer(this, true);
}
