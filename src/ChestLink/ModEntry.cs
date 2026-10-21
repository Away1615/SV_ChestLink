using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace ChestLink;

public sealed class ModEntry : Mod
{
    private StorageConfig config = new();
    private IGenericModConfigMenuApi? configMenu;
    private StorageService? storage;
    private StorageMenuModel? model;
    private IClickableMenu? menu;

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<StorageConfig>();
        bool configChanged = false;
        if (config.SettingsVersion == 0)
        {
            if (config.AutoStoreRange == 3) config.AutoStoreRange = 5;
            config.SettingsVersion = 1;
            configChanged = true;
        }
        int normalizedRange = Math.Clamp(config.AutoStoreRange, 1, 10);
        if (config.AutoStoreRange != normalizedRange)
        {
            config.AutoStoreRange = normalizedRange;
            configChanged = true;
        }
        if (configChanged) helper.WriteConfig(config);
        storage = new StorageService(this);
        TerminalContent.Initialize(this);
        TerminalRecipeLimit.Initialize(this);
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.Content.LocaleChanged += OnLocaleChanged;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        helper.Events.Player.InventoryChanged += OnInventoryChanged;
        helper.Events.World.ChestInventoryChanged += OnChestInventoryChanged;
        helper.Events.World.ObjectListChanged += OnObjectListChanged;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Input.ButtonReleased += OnButtonReleased;
        helper.Events.Display.RenderingHud += OnRenderingHud;
        helper.Events.Display.Rendering += OnRendering;
        helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
        helper.Events.Display.RenderedHud += OnRenderedHud;
    }

    internal string T(string key, object? tokens = null) => Helper.Translation.Get(key, tokens).ToString();
    internal bool LockItemBindingDown => config.LockItemKey.IsDown();
    internal StorageClickAction BoundSlotAction =>
        config.LockItemKey.IsDown() ? StorageClickAction.Lock :
        config.LeaveOneKey.IsDown() ? StorageClickAction.LeaveOne :
        config.TakeHalfKey.IsDown() ? StorageClickAction.TakeHalf :
        config.TakeStackKey.IsDown() ? StorageClickAction.TakeStack :
        config.TakeOneKey.IsDown() ? StorageClickAction.TakeOne : StorageClickAction.None;
    private StorageClickAction PressedSlotAction =>
        config.LockItemKey.JustPressed() ? StorageClickAction.Lock :
        config.LeaveOneKey.JustPressed() ? StorageClickAction.LeaveOne :
        config.TakeHalfKey.JustPressed() ? StorageClickAction.TakeHalf :
        config.TakeStackKey.JustPressed() ? StorageClickAction.TakeStack :
        config.TakeOneKey.JustPressed() ? StorageClickAction.TakeOne : StorageClickAction.None;
    internal void LogInteraction(string message) => Monitor.Log($"[interaction] {message}", LogLevel.Debug);

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e) => TerminalContent.EditAsset(e, this);

    private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
    {
        if (e.OldLocale == e.NewLocale) return;

        Monitor.Log($"Locale changed from '{e.OldLocale}' to '{e.NewLocale}'; reloading localized terminal data.", LogLevel.Trace);
        Helper.GameContent.InvalidateCache("Data/BigCraftables");
        Helper.GameContent.InvalidateCache("Data/CraftingRecipes");
    }

    [EventPriority(EventPriority.High)]
    private void OnRendering(object? sender, RenderingEventArgs e)
    {
        if (IsStorageMenuOpen && menu is NativeStorageMenu nativeMenu)
            nativeMenu.UpdateHoveredItemForExternalTooltip();
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (configMenu is null) return;
        configMenu.Register(ModManifest, () => config = new StorageConfig { SettingsVersion = 1 },
            () => Helper.WriteConfig(config));
        configMenu.AddSectionTitle(ModManifest, () => T("config.shortcuts.title"));
        configMenu.AddKeybindList(ModManifest, () => config.TakeStackKey,
            value => config.TakeStackKey = value, () => T("config.take-stack.name"));
        configMenu.AddKeybindList(ModManifest, () => config.LeaveOneKey,
            value => config.LeaveOneKey = value, () => T("config.leave-one.name"));
        configMenu.AddKeybindList(ModManifest, () => config.TakeOneKey,
            value => config.TakeOneKey = value, () => T("config.take-one.name"));
        configMenu.AddKeybindList(ModManifest, () => config.TakeHalfKey,
            value => config.TakeHalfKey = value, () => T("config.take-half.name"));
        configMenu.AddComplexOption(ModManifest, () => "", (_, _) => { },
            height: () => 24, fieldId: "shortcut-spacer");
        configMenu.AddKeybindList(ModManifest, () => config.QuickStackKey,
            value => config.QuickStackKey = value, () => T("config.quick-stack.name"),
            () => T("config.quick-stack.description"));
        configMenu.AddKeybindList(ModManifest, () => config.NearbyChestQuickStackKey,
            value => config.NearbyChestQuickStackKey = value, () => T("config.nearby-chest-quick-stack.name"),
            () => T("config.nearby-chest-quick-stack.description"));
        configMenu.AddNumberOption(ModManifest, () => config.AutoStoreRange,
            value => config.AutoStoreRange = value, () => T("config.range.name"),
            () => T("config.range.description"), min: 1, max: 10);
        configMenu.AddKeybindList(ModManifest, () => config.LockItemKey,
            value => config.LockItemKey = value, () => T("config.lock.name"));
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        if (Context.IsMainPlayer && TerminalContent.HasExistingTerminal())
            TerminalRecipeLimit.ReceiveCrafted();
        TeachRecipes();
    }
    private void OnDayStarted(object? sender, DayStartedEventArgs e) => TeachRecipes();
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (model is not null && !IsStorageMenuOpen)
        {
            storage?.StopWatchingStorage();
            if (Game1.activeClickableMenu is null) { model = null; menu = null; }
        }
        else if (!Context.IsMainPlayer && IsStorageMenuOpen && storage is not null &&
                 !storage.IsWatchingStorage)
            storage.RequestClientSnapshot(subscribe: true);
        storage?.UpdateRemoteRequest();
        ChestQuickStackAnimation.Update();
    }
    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        TeachRecipes();
        if (Context.IsMainPlayer) TerminalRecipeLimit.SyncForPeer(e.Peer.PlayerID);
    }

    private void OnChestInventoryChanged(object? sender, ChestInventoryChangedEventArgs e)
    {
        if (Context.IsMainPlayer && ChestCatalog.IsOrdinary(e.Chest))
            storage?.NotifyChestInventoryChanged();
    }

    private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
    {
        if (!Context.IsMainPlayer) return;
        if (e.Added.Any(pair => pair.Value.QualifiedItemId == TerminalContent.QualifiedId))
            TerminalRecipeLimit.ReceiveCrafted();
        if (e.Added.Concat(e.Removed).Any(pair => pair.Value is Chest chest && ChestCatalog.IsOrdinary(chest)))
            storage?.NotifyChestObjectsChanged();
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!Context.IsMainPlayer && model is not null &&
            Game1.activeClickableMenu == menu && e.IsLocalPlayer)
            model.Refresh();
    }

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != ModManifest.UniqueID || !Context.IsWorldReady) return;
        if (Context.IsMainPlayer)
        {
            if (e.Type == "storage-snapshot-request")
                storage?.RequestSnapshot(e.FromPlayerID, e.ReadAs<StorageSnapshotRequest>());
            else if (e.Type == "storage-unsubscribe") storage?.HandleUnsubscribe(e.FromPlayerID);
            else if (e.Type == "storage-request")
                storage?.HandleRemoteRequest(e.FromPlayerID, e.ReadAs<StorageRequest>());
            else if (e.Type == "terminal-crafted")
            {
                TerminalRecipeLimit.ReceiveCrafted();
                Helper.Multiplayer.SendMessage(true, "terminal-crafted",
                    new[] { ModManifest.UniqueID });
            }
        }
        else if (e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            if (e.Type == "terminal-crafted") TerminalRecipeLimit.ReceiveCrafted();
            else if (e.Type == "storage-result") storage?.ApplyRemoteResult(e.ReadAs<StorageResult>());
            else if (e.Type == "storage-snapshot")
                storage?.ApplyBroadcastSnapshot(e.ReadAs<StorageSnapshot>(),
                    IsStorageMenuOpen ? model : null);
            else if (e.Type == "storage-snapshot-reply")
                storage?.ApplySnapshotReply(e.ReadAs<StorageSnapshotReply>(),
                    IsStorageMenuOpen ? model : null);
        }
        if (!Context.IsMainPlayer && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID &&
                 e.Type == "storage-pulse")
            ChestQuickStackAnimation.Play(e.ReadAs<StoragePulse>());
    }

    internal void BroadcastStoragePulse(StoragePulse pulse)
    {
        if (pulse.Chests.Count == 0) return;
        ChestQuickStackAnimation.Play(pulse);
        Helper.Multiplayer.SendMessage(pulse, "storage-pulse", new[] { ModManifest.UniqueID });
    }

    internal void SendStorageRequest(StorageRequest request) =>
        Helper.Multiplayer.SendMessage(request, "storage-request", new[] { ModManifest.UniqueID },
            new[] { Game1.MasterPlayer.UniqueMultiplayerID });

    internal void SendStorageSnapshot(long playerId, StorageSnapshot snapshot) =>
        Helper.Multiplayer.SendMessage(snapshot, "storage-snapshot", new[] { ModManifest.UniqueID },
            new[] { playerId });

    internal void SendStorageSnapshotReply(long playerId, StorageSnapshotReply reply) =>
        Helper.Multiplayer.SendMessage(reply, "storage-snapshot-reply",
            new[] { ModManifest.UniqueID }, new[] { playerId });

    internal void SendStorageResult(long playerId, StorageResult result) =>
        Helper.Multiplayer.SendMessage(result, "storage-result", new[] { ModManifest.UniqueID },
            new[] { playerId });

    internal void SendStorageSnapshotRequest(StorageSnapshotRequest request) =>
        Helper.Multiplayer.SendMessage(request, "storage-snapshot-request",
            new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });

    internal void SendStorageUnsubscribe() =>
        Helper.Multiplayer.SendMessage(true, "storage-unsubscribe", new[] { ModManifest.UniqueID },
            new[] { Game1.MasterPlayer.UniqueMultiplayerID });

    internal void BroadcastTerminalCrafted()
    {
        if (Context.IsMainPlayer)
            Helper.Multiplayer.SendMessage(true, "terminal-crafted", new[] { ModManifest.UniqueID });
        else
            Helper.Multiplayer.SendMessage(true, "terminal-crafted", new[] { ModManifest.UniqueID },
                new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }

    internal void SendTerminalCrafted(long peerId) =>
        Helper.Multiplayer.SendMessage(true, "terminal-crafted", new[] { ModManifest.UniqueID },
            new[] { peerId });

    internal bool IsStorageMenuOpen => menu is NativeStorageMenu && Game1.activeClickableMenu == menu;
    internal StorageMenuModel? ActiveStorageModel => IsStorageMenuOpen ? model : null;
    internal ChestCatalog GetStorageCatalog() => storage?.GetCatalog() ?? ChestCatalog.Read();

    private void TeachRecipes()
    {
        if (!Context.IsWorldReady) return;
        // Give the recipe to the local farmer immediately, including farmhands.
        TerminalContent.TeachRecipe(Game1.player);
        if (!Context.IsMainPlayer) return;
        foreach (Farmer farmer in Game1.getAllFarmers()) TerminalContent.TeachRecipe(farmer);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady) return;
        if (config.LockItemKey.JustPressed() &&
            Game1.activeClickableMenu is GameMenu gameMenu &&
            gameMenu.GetCurrentPage() is InventoryPage inventoryPage)
        {
            int x = Game1.getMouseX();
            int y = Game1.getMouseY();
            for (int slot = 0; slot < inventoryPage.inventory.inventory.Count &&
                slot < Game1.player.Items.Count; slot++)
            {
                if (!inventoryPage.inventory.inventory[slot].bounds.Contains(x, y) ||
                    Game1.player.Items[slot] is not { } item) continue;
                ItemLock.Toggle(item);
                Game1.playSound("smallSelect");
                Helper.Input.Suppress(e.Button);
                return;
            }
        }
        if (menu is not null && Game1.activeClickableMenu == menu &&
            e.Button is SButton.MouseLeft or SButton.MouseRight)
            LogInteraction($"SMAPI pressed {e.Button} at ({Game1.getMouseX()},{Game1.getMouseY()})");
        if (menu is NativeStorageMenu nativeMenu && Game1.activeClickableMenu == menu &&
            !nativeMenu.IsSearching &&
            e.Button is not (SButton.MouseLeft or SButton.MouseRight) &&
            (PressedSlotAction != StorageClickAction.None || e.Button == SButton.ControllerA))
        {
            int x = Game1.getMouseX();
            int y = Game1.getMouseY();
            if (PressedSlotAction != StorageClickAction.None &&
                nativeMenu.HandleBoundSlotAction(x, y))
            {
                Helper.Input.Suppress(e.Button);
                return;
            }
            if (e.Button == SButton.ControllerA)
            {
                nativeMenu.receiveLeftClick(x, y);
                nativeMenu.releaseLeftClick(x, y);
                Helper.Input.Suppress(e.Button);
                return;
            }
        }
        if (model is not null && Game1.activeClickableMenu == menu &&
            menu is NativeStorageMenu { IsSearching: false } && config.QuickStackKey.JustPressed())
        {
            storage?.QuickStack(model);
            Helper.Input.Suppress(e.Button);
            return;
        }
        if (Game1.activeClickableMenu is not null) return;

        if (config.NearbyChestQuickStackKey.JustPressed() && IsNearOrdinaryChest())
        {
            storage?.QuickStackNearChests(null);
            Helper.Input.Suppress(e.Button);
            return;
        }

        if (config.QuickStackKey.JustPressed() && IsInAutoStoreRange())
        {
            storage?.QuickStack(null);
            Helper.Input.Suppress(e.Button);
            return;
        }
        if (e.Button is SButton.MouseRight or SButton.ControllerA &&
            IsTerminalAt(e.Cursor.GrabTile) && IsNearTerminal())
        {
            ShowMenu(new StorageMenuModel(this));
            Helper.Input.Suppress(e.Button);
        }
    }

    private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
    {
        if (menu is not null && Game1.activeClickableMenu == menu && e.Button == SButton.MouseLeft)
            LogInteraction($"SMAPI released {e.Button} at ({Game1.getMouseX()},{Game1.getMouseY()})");
    }

    private static bool IsTerminalAt(Microsoft.Xna.Framework.Vector2 tile) =>
        Game1.currentLocation.Objects.TryGetValue(tile, out SObject? obj) &&
        obj.QualifiedItemId == TerminalContent.QualifiedId;

    private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
    {
        if (Context.IsWorldReady && Game1.activeClickableMenu is null &&
            IsTerminalAt(Helper.Input.GetCursorPosition().GrabTile) && IsNearTerminal())
            Game1.mouseCursor = Game1.cursor_grab;
    }

    private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
    {
        if (Game1.activeClickableMenu is not GameMenu gameMenu ||
            gameMenu.GetCurrentPage() is not InventoryPage inventoryPage) return;
        for (int slot = 0; slot < inventoryPage.inventory.inventory.Count &&
            slot < Game1.player.Items.Count; slot++)
            if (Game1.player.Items[slot] is { } item && ItemLock.IsLocked(item))
                LockIcon.Draw(e.SpriteBatch, inventoryPage.inventory.inventory[slot].bounds);
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.activeClickableMenu is not null) return;
        Toolbar? toolbar = Game1.onScreenMenus.OfType<Toolbar>().FirstOrDefault();
        if (toolbar is null) return;
        int count = Math.Min(Game1.player.Items.Count, toolbar.buttons.Count);
        for (int slot = 0; slot < count; slot++)
            if (Game1.player.Items[slot] is { } item && ItemLock.IsLocked(item))
                LockIcon.Draw(e.SpriteBatch, toolbar.buttons[slot].bounds);
    }

    internal bool IsNearTerminal() => IsNearTerminal(3);

    internal bool IsNearTerminal(Farmer player) => IsNearTerminal(player, 3);

    internal bool IsInAutoStoreRange() => IsNearTerminal(config.AutoStoreRange);

    internal bool IsInAutoStoreRange(Farmer player) => IsNearTerminal(player, config.AutoStoreRange);

    internal bool IsNearOrdinaryChest() => IsNearOrdinaryChest(Game1.player);

    internal bool IsNearOrdinaryChest(Farmer player)
    {
        if (!Context.IsWorldReady || player.currentLocation is not { } location) return false;
        int range = Math.Clamp(config.AutoStoreRange, 1, 10);
        return location.Objects.Pairs.Any(pair => pair.Value is StardewValley.Objects.Chest chest &&
            ChestCatalog.IsOrdinary(chest) &&
            Microsoft.Xna.Framework.Vector2.DistanceSquared(player.Tile, pair.Key) <= range * range);
    }

    internal IReadOnlyList<ChestSource> GetNearbyOrdinaryChests(Farmer player, ChestCatalog catalog)
    {
        if (player.currentLocation is not { } location) return Array.Empty<ChestSource>();
        int range = Math.Clamp(config.AutoStoreRange, 1, 10);
        return catalog.Chests.Where(source => source.Location.NameOrUniqueName == location.NameOrUniqueName &&
            Microsoft.Xna.Framework.Vector2.DistanceSquared(player.Tile, source.Tile) <= range * range).ToList();
    }

    private static bool IsNearTerminal(int configuredRange) => IsNearTerminal(Game1.player, configuredRange);

    private static bool IsNearTerminal(Farmer player, int configuredRange)
    {
        if (!Context.IsWorldReady) return false;
        var playerTile = player.Tile;
        int range = Math.Clamp(configuredRange, 1, 10);
        return player.currentLocation is not null && player.currentLocation.Objects.Pairs.Any(pair =>
            pair.Value.QualifiedItemId == TerminalContent.QualifiedId &&
            Microsoft.Xna.Framework.Vector2.DistanceSquared(playerTile, pair.Key) <= range * range);
    }

    internal void ShowMenu(StorageMenuModel menuModel)
    {
        model = menuModel;
        if (!Context.IsMainPlayer) storage?.ApplyCachedSnapshot(menuModel);
        menu = new NativeStorageMenu(menuModel);
        Game1.activeClickableMenu = menu;
        if (!Context.IsMainPlayer) storage?.RequestClientSnapshot(subscribe: true);
    }

    internal void RefreshOpenStorageMenu(ChestCatalog? updatedCatalog = null)
    {
        if (Game1.activeClickableMenu != menu || model is null) return;
        if (updatedCatalog is not null) model.RefreshFromCatalog(updatedCatalog);
        else model.Refresh();
    }

    internal void Transfer(StorageMenuModel menuModel, StorageSlot slot, int amount) =>
        storage?.Transfer(menuModel, slot, amount);

    internal void QuickStack(StorageMenuModel menuModel) => storage?.QuickStack(menuModel);

    internal bool HasConfigMenu => configMenu is not null;

    internal bool OpenConfigMenu()
    {
        if (configMenu is null) return false;
        configMenu.OpenModMenuAsChildMenu(ModManifest);
        return true;
    }

}
