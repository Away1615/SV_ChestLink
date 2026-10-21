using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace ChestLink;

/// <summary>A vanilla game menu backed by the existing storage view model.</summary>
internal sealed class NativeStorageMenu : ItemGrabMenu
{
    private const int MenuWidth = 840;
    private const int MenuHeight = 760;
    private const int StorageCapacity = 60;
    private const int BackpackPanelTop = 480;
    private const int HotbarNumberHeight = 16;
    private const int TabWidth = 64;
    private const int TabHeight = 64;
    private const int TabStep = 60;
    private const int TabIconSize = 48;
    private const int SelectedTabShift = 4;
    private const int SideButtonGap = 16;
    private const int SearchRowInset = 36;

    private readonly StorageMenuModel model;
    private InventoryMenu? storageInventory;
    private InventoryMenu? backpackInventory;
    private List<StorageSlot>? visibleStorageSlots;
    private List<StorageSlot>? visibleBackpackSlots;
    private int hoveredStorage = -1;
    private int hoveredBackpack = -1;
    private int hoveredCategory = -1;
    private int hoveredContainer = -1;
    private string hoveredAction = "";
    private bool communityItemWasHovered;
    private int pressedBackpackIndex = -1;
    private int pressedBackpackAmount;
    private StorageSlot? pressedStorageSlot;
    private int pressedStorageAmount;
    private Point pressPoint;
    private bool trashMode;
    private ClickableTextureComponent? storageTrashCan;
    private Rectangle okButtonBounds;
    private ClickableTextureComponent? storageOkButton;
    private readonly ClickableTextureComponent[] actionButtons = new ClickableTextureComponent[6];
    private float trashLidRotation;
    private readonly TextBox searchBox;
    internal bool IsSearching => searchBox.Selected;

    internal NativeStorageMenu(StorageMenuModel model)
        : base(new List<Item>(), null!)
    {
        this.model = model;
        xPositionOnScreen = (Game1.uiViewport.Width - MenuWidth) / 2;
        yPositionOnScreen = (Game1.uiViewport.Height - MenuHeight) / 2;
        width = MenuWidth;
        height = MenuHeight;
        searchBox = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"),
            null, Game1.smallFont, Game1.textColor)
        {
            X = xPositionOnScreen + SearchRowInset,
            Y = yPositionOnScreen + 40,
            Width = MenuWidth - 2 * SearchRowInset,
            Text = model.SearchText
        };
        searchBox.OnEnterPressed += _ => CommitSearch();
        RebuildInventories();
        CreateActionButtons();
        CreateTrashCan();
    }

    private Rectangle TabBounds(int index, bool selected) =>
        new Rectangle(xPositionOnScreen + 28 + index * TabStep,
            yPositionOnScreen - TabHeight + (selected ? SelectedTabShift : 0), TabWidth, TabHeight);

    private Rectangle ContainerTabBounds(int index, bool selected) =>
        new Rectangle(xPositionOnScreen - 56 + (selected ? SelectedTabShift : 0),
            yPositionOnScreen + 120 + index * (TabHeight + 8), TabWidth, TabHeight);

    private Rectangle ClearSearchBounds() => new(searchBox.X + searchBox.Width - 44,
        searchBox.Y + 4, 40, 40);

    private void CommitSearch()
    {
        model.SetSearch(searchBox.Text);
        searchBox.Selected = false;
        Game1.playSound("smallSelect");
    }

    private Rectangle ActionBounds(int index) => new(
        xPositionOnScreen + width + 24,
        yPositionOnScreen + 96 + index * (64 + SideButtonGap), 64, 64);

    private void CreateTrashCan()
    {
        storageTrashCan = new ClickableTextureComponent(
            new Rectangle(ActionBounds(5).X, ActionBounds(5).Bottom + SideButtonGap, 64, 104),
            Game1.mouseCursors,
            new Rectangle(564 + Game1.player.trashCanLevel * 18, 102, 18, 26), 4f);
        okButtonBounds = new Rectangle(storageTrashCan.bounds.X, storageTrashCan.bounds.Bottom + SideButtonGap, 64, 64);
        storageOkButton = new ClickableTextureComponent(okButtonBounds, Game1.mouseCursors,
            Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 46), 1f);
    }

    private void CreateActionButtons()
    {
        actionButtons[0] = new ClickableTextureComponent(ActionBounds(0), Game1.mouseCursors,
            new Rectangle(331, 374, 15, 14), 4f);
        actionButtons[1] = new ClickableTextureComponent(ActionBounds(1), Game1.mouseCursors,
            new Rectangle(162, 440, 16, 16), 4f);
        actionButtons[2] = new ClickableTextureComponent(ActionBounds(2), Game1.mouseCursors,
            new Rectangle(103, 469, 16, 16), 4f);
        foreach (int index in new[] { 3, 4 })
            actionButtons[index] = new ClickableTextureComponent(ActionBounds(index), Game1.mouseCursors,
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, index == 3 ? 12 : 11), 1f);
        actionButtons[5] = new ClickableTextureComponent(ActionBounds(5), Game1.mouseCursors,
            new Rectangle(96, 368, 16, 16), 4f);
    }

    private void EnsureInventories()
    {
        if (!ReferenceEquals(visibleStorageSlots, model.StorageSlots) ||
            !ReferenceEquals(visibleBackpackSlots, model.BackpackSlots))
            RebuildInventories();
    }

    private void RebuildInventories()
    {
        visibleStorageSlots = model.StorageSlots;
        visibleBackpackSlots = model.BackpackSlots;
        hoveredStorage = -1;
        hoveredBackpack = -1;
        var displayItems = new List<Item>(StorageCapacity);
        foreach (StorageSlot slot in visibleStorageSlots)
        {
            if (slot.Item is null) { displayItems.Add(null!); continue; }
            Item display = slot.Item.getOne();
            display.Stack = slot.Count;
            displayItems.Add(display);
        }
        storageInventory = new InventoryMenu(xPositionOnScreen + 36, yPositionOnScreen + 128,
            false, displayItems, capacity: StorageCapacity, rows: 5);
        backpackInventory = new InventoryMenu(xPositionOnScreen + 36, yPositionOnScreen + 528,
            true, Game1.player.Items, capacity: Game1.player.MaxItems,
            rows: Math.Max(1, Game1.player.MaxItems / 12)) { showGrayedOutSlots = true };
        if (backpackInventory.inventory.Count > 0)
        {
            // The hotbar numbers belong to the backpack content when balancing panel padding.
            int contentTop = backpackInventory.inventory.Min(slot => slot.bounds.Top) - HotbarNumberHeight;
            int contentBottom = backpackInventory.inventory.Max(slot => slot.bounds.Bottom);
            int panelCenter = yPositionOnScreen + (BackpackPanelTop + height) / 2;
            int contentCenter = (contentTop + contentBottom) / 2;
            backpackInventory.SetPosition(backpackInventory.xPositionOnScreen,
                backpackInventory.yPositionOnScreen + panelCenter - contentCenter);
        }
    }

    // InventoryMenu draws its slots from these components. Use their actual bounds for
    // interaction as well, so hit testing stays aligned with the visible grid.
    private static int SlotAt(InventoryMenu? inventory, int x, int y)
    {
        if (inventory is null) return -1;
        for (int i = 0; i < inventory.inventory.Count; i++)
            if (inventory.inventory[i].bounds.Contains(x, y)) return i;
        return -1;
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        xPositionOnScreen = (newBounds.Width - MenuWidth) / 2;
        yPositionOnScreen = (newBounds.Height - MenuHeight) / 2;
        searchBox.X = xPositionOnScreen + SearchRowInset;
        searchBox.Y = yPositionOnScreen + 40;
        RebuildInventories();
        CreateActionButtons();
        CreateTrashCan();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        model.LogInteraction($"left press ({x},{y}); storage={SlotAt(storageInventory, x, y)}, " +
            $"backpack={SlotAt(backpackInventory, x, y)}, searchHeight={searchBox.Height}, " +
            $"firstStorageBounds={(storageInventory?.inventory.Count > 0 ? storageInventory.inventory[0].bounds.ToString() : "<none>")}");
        pressedStorageSlot = null;
        pressedBackpackIndex = -1;
        if (okButtonBounds.Contains(x, y))
        {
            exitThisMenu();
            return;
        }
        if (ClearSearchBounds().Contains(x, y))
        {
            searchBox.Text = "";
            model.SetSearch("");
            searchBox.Selected = true;
            Game1.playSound("smallSelect");
            return;
        }
        if (new Rectangle(searchBox.X, searchBox.Y, searchBox.Width, searchBox.Height).Contains(x, y))
        {
            searchBox.Selected = true;
            if (Game1.options.gamepadControls && !Game1.lastCursorMotionWasMouse)
                Game1.showTextEntry(searchBox);
            return;
        }
        searchBox.Selected = false;
        if (storageTrashCan?.containsPoint(x, y) == true)
        {
            trashMode = !trashMode;
            Game1.playSound("smallSelect");
            return;
        }
        EnsureInventories();
        for (int i = 0; i < model.Containers.Count; i++)
        {
            if (!ContainerTabBounds(i, model.Containers[i].IsSelected).Contains(x, y)) continue;
            model.Containers[i].Select();
            Game1.playSound("smallSelect");
            return;
        }
        for (int i = 0; i < model.Categories.Count; i++)
        {
            if (!TabBounds(i, model.Categories[i].IsSelected).Contains(x, y)) continue;
            model.Categories[i].Select();
            searchBox.Text = "";
            Game1.playSound("smallSelect");
            return;
        }
        if (ActionBounds(0).Contains(x, y))
        {
            if (InventoryPage.ShouldShowJunimoNoteIcon())
            {
                Game1.playSound("smallSelect");
                Game1.activeClickableMenu = new JunimoNoteMenu(true, 0, true);
            }
            else model.SetStatus(model.T("status.community-locked"));
            return;
        }
        if (ActionBounds(1).Contains(x, y))
        {
            ItemGrabMenu.organizeItemsInList(Game1.player.Items);
            model.Refresh();
            Game1.playSound("smallSelect");
            return;
        }
        if (ActionBounds(2).Contains(x, y)) { model.QuickStack(); Game1.playSound("smallSelect"); return; }
        if (ActionBounds(3).Contains(x, y)) { model.PreviousPage(); Game1.playSound("smallSelect"); return; }
        if (ActionBounds(4).Contains(x, y)) { model.NextPage(); Game1.playSound("smallSelect"); return; }
        if (ActionBounds(5).Contains(x, y))
        {
            if (model.OpenConfigMenu()) Game1.playSound("smallSelect");
            else model.SetStatus(model.T("status.gmcm-required"));
            return;
        }

        int storageIndex = SlotAt(storageInventory, x, y);
        if (storageIndex >= 0 && storageIndex < model.StorageSlots.Count)
        {
            StorageSlot slot = model.StorageSlots[storageIndex];
            if (slot.Item is null)
            {
                model.LogInteraction($"storage slot {storageIndex} is empty");
                return;
            }
            StorageClickAction action = model.BoundSlotAction;
            if (action is StorageClickAction.None or StorageClickAction.Lock) return;
            pressedStorageSlot = slot;
            pressedStorageAmount = AmountFor(action, slot.Count);
            pressPoint = new Point(x, y);
            model.LogInteraction($"storage slot {storageIndex} armed: {slot.Item.QualifiedItemId} " +
                $"x{pressedStorageAmount}");
            return;
        }
        int backpackIndex = SlotAt(backpackInventory, x, y);
        if (backpackIndex >= 0 && backpackIndex < model.BackpackSlots.Count)
        {
            StorageClickAction action = model.BoundSlotAction;
            if (action == StorageClickAction.Lock)
            {
                Item? item = backpackIndex < Game1.player.Items.Count
                    ? Game1.player.Items[backpackIndex] : null;
                if (item is not null)
                {
                    ItemLock.Toggle(item);
                    model.SetStatus("");
                    Game1.playSound("smallSelect");
                }
                return;
            }
            if (trashMode)
            {
                TrashBackpackItem(backpackIndex);
                trashMode = false;
            }
            else if (action != StorageClickAction.None)
            {
                pressedBackpackIndex = backpackIndex;
                // The leave-one shortcut applies to withdrawing from storage;
                // depositing from the bag keeps the original full-stack behavior.
                pressedBackpackAmount = action == StorageClickAction.LeaveOne
                    ? model.BackpackSlots[backpackIndex].Count
                    : AmountFor(action, model.BackpackSlots[backpackIndex].Count);
                pressPoint = new Point(x, y);
            }
        }
    }

    public override void releaseLeftClick(int x, int y)
    {
        model.LogInteraction($"left release ({x},{y}); storageArmed={pressedStorageSlot is not null}, " +
            $"backpackArmed={pressedBackpackIndex}");
        if (pressedStorageSlot is { } storageSlot)
        {
            pressedStorageSlot = null;
            // A click should take the selected stack even if the pointer moves a few
            // pixels beyond the slot before release. The service places it in the bag.
            model.Transfer(storageSlot, pressedStorageAmount);
            return;
        }
        if (pressedBackpackIndex < 0) return;
        int index = pressedBackpackIndex;
        pressedBackpackIndex = -1;
        EnsureInventories();
        if (storageTrashCan?.containsPoint(x, y) == true)
            TrashBackpackItem(index);
        else if ((SlotAt(backpackInventory, x, y) == index ||
                 SlotAt(storageInventory, x, y) >= 0) &&
                 index < model.BackpackSlots.Count)
            model.Transfer(model.BackpackSlots[index], pressedBackpackAmount);
    }

    private void TrashBackpackItem(int index)
    {
        if (index < 0 || index >= Game1.player.Items.Count) return;
        Item? item = Game1.player.Items[index];
        if (item is null || !item.canBeTrashed())
        {
            model.SetStatus(model.T("status.cannot-trash"));
            return;
        }
        // The native routine handles trash-can salvage value and sound.
        Game1.player.Items[index] = null;
        Utility.trashItem(item);
        model.Refresh();
    }

    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
        EnsureInventories();
        int storageIndex = SlotAt(storageInventory, x, y);
        model.LogInteraction($"right press ({x},{y}); storage={storageIndex}, " +
            $"backpack={SlotAt(backpackInventory, x, y)}");
        if (storageIndex >= 0 && storageIndex < model.StorageSlots.Count)
        {
            StorageSlot slot = model.StorageSlots[storageIndex];
            StorageClickAction action = model.BoundSlotAction;
            if (action is not (StorageClickAction.None or StorageClickAction.Lock))
                model.Transfer(slot, AmountFor(action, slot.Count));
            return;
        }
        int backpackIndex = SlotAt(backpackInventory, x, y);
        if (backpackIndex >= 0 && backpackIndex < model.BackpackSlots.Count)
        {
            StorageSlot slot = model.BackpackSlots[backpackIndex];
            StorageClickAction action = model.BoundSlotAction;
            if (action is not (StorageClickAction.None or StorageClickAction.Lock))
                model.Transfer(slot, AmountFor(action, slot.Count));
        }
    }

    internal bool HandleBoundSlotAction(int x, int y)
    {
        EnsureInventories();
        if (SlotAt(storageInventory, x, y) < 0 && SlotAt(backpackInventory, x, y) < 0)
            return false;
        receiveLeftClick(x, y);
        releaseLeftClick(x, y);
        return true;
    }

    private static int AmountFor(StorageClickAction action, int count) => action switch
    {
        StorageClickAction.LeaveOne => Math.Max(0, count - 1),
        StorageClickAction.TakeOne => 1,
        StorageClickAction.TakeHalf => (count + 1) / 2,
        _ => count
    };

    public override void receiveScrollWheelAction(int direction)
    {
        if (direction > 0) model.PreviousPage();
        else if (direction < 0) model.NextPage();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape && searchBox.Selected)
        {
            searchBox.Selected = false;
            return;
        }
        if (!searchBox.Selected && Game1.options.doesInputListContain(Game1.options.menuButton, key) && readyToClose())
            exitThisMenu();
    }

    public override void update(GameTime time)
    {
        if (searchBox.Text != model.SearchText)
            model.SetSearch(searchBox.Text);
    }

    public override void receiveGamePadButton(Buttons button) { }

    public override void performHoverAction(int x, int y)
    {
        EnsureInventories();
        hoveredStorage = SlotAt(storageInventory, x, y);
        hoveredBackpack = SlotAt(backpackInventory, x, y);
        UpdateHoveredItemForExternalTooltip();
        hoveredCategory = -1;
        hoveredContainer = -1;
        hoveredAction = "";
        for (int i = 0; i < model.Containers.Count; i++)
            if (ContainerTabBounds(i, model.Containers[i].IsSelected).Contains(x, y))
            { hoveredContainer = i; break; }
        for (int i = 0; i < model.Categories.Count; i++)
            if (TabBounds(i, model.Categories[i].IsSelected).Contains(x, y)) { hoveredCategory = i; break; }
        for (int i = 0; i < actionButtons.Length; i++)
            if (ActionBounds(i).Contains(x, y))
                hoveredAction = i switch
                {
                    0 => model.T(InventoryPage.ShouldShowJunimoNoteIcon() ?
                        "action.community" : "action.community-locked"),
                    1 => model.T("action.organize"), 2 => model.T("action.quick-stack"),
                    3 => model.T("action.previous"), 4 => model.T("action.next"),
                    _ => model.T(model.HasConfigMenu ? "action.settings" : "action.settings-unavailable")
                };
        if (storageTrashCan?.containsPoint(x, y) == true)
            hoveredAction = model.T(trashMode ? "action.trash-active" : "action.trash");
        if (okButtonBounds.Contains(x, y)) hoveredAction = model.T("action.close");
        storageTrashCan?.tryHover(x, y);
        storageOkButton?.tryHover(x, y);
        for (int i = 0; i < actionButtons.Length; i++)
            if ((i != 0 || InventoryPage.ShouldShowJunimoNoteIcon()) &&
                (i != 5 || model.HasConfigMenu))
                actionButtons[i].tryHover(x, y);
        UpdateCommunityCenterButtonPulse(x, y);
        bool openTrash = storageTrashCan?.containsPoint(x, y) == true;
        trashLidRotation = MathHelper.Clamp(trashLidRotation + (openTrash ? 0.08f : -0.08f),
            0f, MathHelper.PiOver2);
    }

    private void UpdateCommunityCenterButtonPulse(int x, int y)
    {
        GameMenu.bundleItemHovered = false;
        if (InventoryPage.ShouldShowJunimoNoteIcon())
        {
            if (hoveredStorage >= 0)
                storageInventory?.hover(x, y, heldItem);
            else if (hoveredBackpack >= 0)
                backpackInventory?.hover(x, y, heldItem);
        }

        ClickableTextureComponent button = actionButtons[0];
        if (GameMenu.bundleItemHovered)
        {
            if (!communityItemWasHovered)
            {
                // Match the vanilla Junimo Note page-switch cue. Set this cue to full
                // volume without changing the player's global sound settings.
                Game1.playSound("shwip", out ICue cue);
                if (cue is not null)
                    cue.Volume = 1f;
            }

            // Keep the vanilla Junimo Note rhythm, with a stronger scale range so it
            // reads clearly beside the storage grid. InventoryMenu.hover above uses
            // CommunityCenter.couldThisIngredienteBeUsedInABundle, so completed
            // donations and items which can't fill a remaining bundle don't pulse.
            button.scale = button.baseScale + (float)Math.Sin(junimoNotePulser / 100.0) * 0.75f;
            junimoNotePulser += (int)Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
        }
        else
        {
            junimoNotePulser = 0;
            button.scale = button.baseScale;
        }
        communityItemWasHovered = GameMenu.bundleItemHovered;
    }

    internal void UpdateHoveredItemForExternalTooltip()
    {
        EnsureInventories();
        Point mouse = new(Game1.getMouseX(), Game1.getMouseY());
        hoveredStorage = SlotAt(storageInventory, mouse.X, mouse.Y);
        hoveredBackpack = SlotAt(backpackInventory, mouse.X, mouse.Y);
        hoveredItem = hoveredStorage >= 0 && hoveredStorage < model.StorageSlots.Count
            ? CreateHoverItem(model.StorageSlots[hoveredStorage])
            : hoveredBackpack >= 0 && hoveredBackpack < model.BackpackSlots.Count
                ? model.BackpackSlots[hoveredBackpack].Item : null;
    }

    public override void draw(SpriteBatch b)
    {
        performHoverAction(Game1.getMouseX(), Game1.getMouseY());
        EnsureInventories();
        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height),
            Color.Black * 0.5f);
        for (int i = 0; i < model.Categories.Count; i++)
            if (!model.Categories[i].IsSelected) DrawTab(b, i, false);
        for (int i = 0; i < model.Containers.Count; i++)
            if (!model.Containers[i].IsSelected) DrawContainerTab(b, i, false);

        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, 492, Color.White);
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen + BackpackPanelTop,
            width, height - BackpackPanelTop, Color.White);
        searchBox.Draw(b);
        DrawCapacity(b);
        if (searchBox.Text.Length == 0 && !searchBox.Selected)
            b.DrawString(Game1.smallFont, model.T("search.placeholder"),
                new Vector2(searchBox.X + 16, searchBox.Y + 12), Color.Gray);
        if (searchBox.Text.Length > 0)
        {
            Rectangle clear = ClearSearchBounds();
            b.DrawString(Game1.smallFont, "×", new Vector2(clear.X + 8, clear.Y + 2), Game1.textColor);
        }
        storageInventory?.draw(b);
        DrawStorageOverlays(b);
        backpackInventory?.draw(b);
        DrawBackpackLocks(b);
        if (!string.IsNullOrEmpty(model.Status))
            b.DrawString(Game1.smallFont, model.Status,
                new Vector2(xPositionOnScreen + 36, yPositionOnScreen + 455), Game1.textColor);

        for (int i = 0; i < model.Categories.Count; i++)
            if (model.Categories[i].IsSelected) DrawTab(b, i, true);
        for (int i = 0; i < model.Containers.Count; i++)
            if (model.Containers[i].IsSelected) DrawContainerTab(b, i, true);
        DrawActions(b);
        DrawHover(b);
        DrawDraggedItem(b);
        drawMouse(b);
    }

    private void DrawTab(SpriteBatch b, int index, bool selected)
    {
        Rectangle bounds = TabBounds(index, selected);
        if (bounds == Rectangle.Empty) return;
        b.Draw(Game1.mouseCursors, new Vector2(bounds.X, bounds.Y),
            new Rectangle(16, 368, 16, 16), Color.White,
            0f, Vector2.Zero, 4f, SpriteEffects.None, 0f);
        var icon = model.Categories[index].Icon;
        if (icon is not null)
        {
            Rectangle source = icon.GetSourceRect();
            float scale = Math.Min((float)TabIconSize / source.Width,
                (float)TabIconSize / source.Height);
            int iconWidth = (int)Math.Round(source.Width * scale);
            int iconHeight = (int)Math.Round(source.Height * scale);
            b.Draw(icon.GetTexture(), new Rectangle(bounds.Center.X - iconWidth / 2,
                bounds.Center.Y - iconHeight / 2, iconWidth, iconHeight), source, Color.White);
        }
    }

    private void DrawContainerTab(SpriteBatch b, int index, bool selected)
    {
        Rectangle bounds = ContainerTabBounds(index, selected);
        // The vanilla tab sprite opens at its bottom; rotate it so the opening
        // joins the storage panel on the right.
        b.Draw(Game1.mouseCursors, new Vector2(bounds.X, bounds.Bottom),
            new Rectangle(16, 368, 16, 16), Color.White,
            -MathHelper.PiOver2, Vector2.Zero, 4f, SpriteEffects.None, 0f);
        Rectangle iconArea = new(bounds.X + (TabWidth - TabIconSize) / 2,
            bounds.Y + (TabHeight - TabIconSize) / 2, TabIconSize, TabIconSize);
        if (!DrawContainerIcon(b, model.Containers[index].Icon, iconArea))
            DrawContainerLabel(b, model.Containers[index].Name,
                iconArea);
    }

    private static bool DrawContainerIcon(SpriteBatch b,
        StardewValley.ItemTypeDefinitions.ParsedItemData? icon, Rectangle area)
    {
        if (icon is null) return false;
        Rectangle source = icon.GetSourceRect();
        float scale = Math.Min((float)area.Width / source.Width,
            (float)area.Height / source.Height);
        int width = (int)Math.Round(source.Width * scale);
        int height = (int)Math.Round(source.Height * scale);
        b.Draw(icon.GetTexture(), new Rectangle(area.Center.X - width / 2,
            area.Center.Y - height / 2, width, height), source, Color.White);
        return true;
    }

    private static void DrawContainerLabel(SpriteBatch b, string label, Rectangle area)
    {
        string initial = label.Length > 0 ? label[..1] : "?";
        Vector2 size = Game1.smallFont.MeasureString(initial);
        b.DrawString(Game1.smallFont, initial,
            new Vector2(area.Center.X - size.X / 2, area.Center.Y - size.Y / 2),
            Game1.textColor);
    }

    private void DrawDraggedItem(SpriteBatch b)
    {
        Item? item = pressedStorageSlot?.Item ??
            (pressedBackpackIndex >= 0 && pressedBackpackIndex < model.BackpackSlots.Count
                ? model.BackpackSlots[pressedBackpackIndex].Item : null);
        if (item is null) return;
        Point mouse = new(Game1.getMouseX(), Game1.getMouseY());
        if (Math.Abs(mouse.X - pressPoint.X) + Math.Abs(mouse.Y - pressPoint.Y) < 12) return;
        Item preview = item.getOne();
        preview.Stack = pressedStorageSlot is null ? pressedBackpackAmount : pressedStorageAmount;
        preview.drawInMenu(b, new Vector2(mouse.X - 32, mouse.Y - 32), 1f, 0.85f, 1f,
            StackDrawType.Draw, Color.White, false);
    }

    private void DrawStorageOverlays(SpriteBatch b)
    {
        if (storageInventory is null) return;
        for (int i = 0; i < model.StorageSlots.Count && i < storageInventory.inventory.Count; i++)
        {
            StorageSlot slot = model.StorageSlots[i];
            if (slot.Item is null) continue;
            Rectangle bounds = storageInventory.inventory[i].bounds;
            if (slot.SourceSprite is { } source)
                b.Draw(source.GetTexture(), new Rectangle(bounds.Right - 31, bounds.Y + 2, 28, 28),
                    source.GetSourceRect(), Color.White);
        }
    }

    private void DrawBackpackLocks(SpriteBatch b)
    {
        if (backpackInventory is null) return;
        for (int i = 0; i < backpackInventory.inventory.Count && i < Game1.player.Items.Count; i++)
            if (Game1.player.Items[i] is { } item && ItemLock.IsLocked(item))
                LockIcon.Draw(b, backpackInventory.inventory[i].bounds);
    }

    private void DrawCapacity(SpriteBatch b)
    {
        string page = $"{model.PageNumber}/{model.PageCount}";
        string label = model.T("capacity.status", new
        {
            used = model.OccupiedChestSlots,
            capacity = model.TotalChestSlots
        });
        float y = searchBox.Y + searchBox.Height + 2;
        b.DrawString(Game1.smallFont, label,
            new Vector2(xPositionOnScreen + SearchRowInset, y), Game1.textColor);
        Vector2 size = Game1.smallFont.MeasureString(page);
        b.DrawString(Game1.smallFont, page,
            new Vector2(xPositionOnScreen + width - SearchRowInset - size.X, y), Game1.textColor);
    }

    private void DrawActions(SpriteBatch b)
    {
        for (int i = 0; i < actionButtons.Length - 1; i++)
            actionButtons[i].draw(b, i == 0 && !InventoryPage.ShouldShowJunimoNoteIcon()
                ? Color.Gray : Color.White, 0.86f);
        Rectangle settings = ActionBounds(5);
        Color settingsColor = model.HasConfigMenu ? Color.White : Color.Gray;
        drawTextureBox(b, settings.X, settings.Y, settings.Width, settings.Height,
            settings.Contains(Game1.getMouseX(), Game1.getMouseY()) && model.HasConfigMenu
                ? Color.Wheat : settingsColor);
        b.Draw(Game1.mouseCursors, new Rectangle(settings.X + 10, settings.Y + 12, 44, 40),
            new Rectangle(99, 371, 11, 10), settingsColor);
        storageTrashCan?.draw(b, trashMode ? Color.LightGoldenrodYellow : Color.White, 0.86f);
        if (storageTrashCan is not null)
            b.Draw(Game1.mouseCursors,
                new Vector2(storageTrashCan.bounds.X + 60, storageTrashCan.bounds.Y + 40),
                new Rectangle(564 + Game1.player.trashCanLevel * 18, 129, 18, 10),
                Color.White, trashLidRotation, new Vector2(16, 10), 4f,
                SpriteEffects.None, 0.86f);
        storageOkButton?.draw(b);
    }

    private void DrawHover(SpriteBatch b)
    {
        if (hoveredContainer >= 0)
        {
            drawHoverText(b, model.Containers[hoveredContainer].Name, Game1.smallFont);
            return;
        }
        if (hoveredCategory >= 0)
        {
            drawHoverText(b, model.Categories[hoveredCategory].Name, Game1.smallFont);
            return;
        }
        if (hoveredAction.Length > 0)
        {
            drawHoverText(b, hoveredAction, Game1.smallFont);
            return;
        }
        StorageSlot? slot = hoveredStorage >= 0 && hoveredStorage < model.StorageSlots.Count
            ? model.StorageSlots[hoveredStorage]
            : hoveredBackpack >= 0 && hoveredBackpack < model.BackpackSlots.Count
                ? model.BackpackSlots[hoveredBackpack] : null;
        if (slot?.Item is not null)
            drawToolTip(b, slot.Item.getDescription(), slot.Item.DisplayName, slot.Item);
    }

    private static Item? CreateHoverItem(StorageSlot slot)
    {
        if (slot.Item is null) return null;
        Item display = slot.Item.getOne();
        display.Stack = Math.Max(1, slot.Count);
        return display;
    }
}
