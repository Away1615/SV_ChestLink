using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ChestLink;

public sealed class StorageConfig
{
    public int AutoStoreRange { get; set; } = 5;
    public int SettingsVersion { get; set; }
    public KeybindList QuickStackKey { get; set; } = new(SButton.K);
    public KeybindList NearbyChestQuickStackKey { get; set; } = new(SButton.L);
    public KeybindList TakeStackKey { get; set; } = new(new Keybind(SButton.MouseLeft), new Keybind(SButton.ControllerA));
    public KeybindList LeaveOneKey { get; set; } = new(new Keybind(SButton.LeftShift, SButton.MouseLeft), new Keybind(SButton.RightShoulder, SButton.ControllerA));
    public KeybindList TakeOneKey { get; set; } = new(new Keybind(SButton.MouseRight), new Keybind(SButton.ControllerX));
    public KeybindList TakeHalfKey { get; set; } = new(new Keybind(SButton.LeftShift, SButton.MouseRight), new Keybind(SButton.RightShoulder, SButton.ControllerX));
    public KeybindList LockItemKey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.MouseLeft), new Keybind(SButton.LeftShoulder, SButton.ControllerA));
}
