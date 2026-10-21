using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ChestLink;

// The small part of Generic Mod Config Menu's public SMAPI API used by this mod.
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue,
        Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null,
        int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null);
    void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);
    void AddKeybind(IManifest mod, Func<SButton> getValue, Action<SButton> setValue,
        Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue,
        Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddComplexOption(IManifest mod, Func<string> name, Action<SpriteBatch, Vector2> draw,
        Func<string>? tooltip = null, Action? beforeMenuOpened = null, Action? beforeSave = null,
        Action? afterSave = null, Action? beforeReset = null, Action? afterReset = null,
        Action? beforeMenuClosed = null, Func<int>? height = null, string? fieldId = null);
    void OpenModMenuAsChildMenu(IManifest mod);
}
