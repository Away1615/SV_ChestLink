using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.Objects;
using Microsoft.Xna.Framework.Graphics;

namespace ChestLink;

internal static class TerminalContent
{
    internal const string Id = "Nooobad.ChestLink_Terminal";
    internal const string QualifiedId = "(BC)" + Id;
    private static ModEntry? mod;
    internal static string RecipeName => mod?.T("terminal.name") ?? "Ancient Storage Terminal";
    internal const string TextureAssetName = "Mods/Nooobad.ChestLink/Terminal";

    internal static void Initialize(ModEntry entry) => mod = entry;

    internal static void EditAsset(AssetRequestedEventArgs e, ModEntry mod)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(TextureAssetName))
        {
            e.LoadFromModFile<Texture2D>("assets/terminal.png", AssetLoadPriority.Medium);
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))
        {
            e.Edit(asset => asset.AsDictionary<string, BigCraftableData>().Data[Id] = new BigCraftableData
            {
                Name = RecipeName,
                DisplayName = mod.T("terminal.name"),
                Description = mod.T("terminal.description"),
                Fragility = 0,
                CanBePlacedIndoors = true,
                CanBePlacedOutdoors = true,
                Texture = TextureAssetName,
                SpriteIndex = 0
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
        {
            // 388 is wood, 336 gold bar, and 338 refined quartz.
            e.Edit(asset => asset.AsDictionary<string, string>().Data[RecipeName] =
                $"388 20 336 5 338 5/Home/{Id}/true/default/");
        }
    }

    internal static void TeachRecipe(Farmer farmer)
    {
        if (!farmer.craftingRecipes.ContainsKey(RecipeName))
            farmer.craftingRecipes.Add(RecipeName, 0);
    }

    internal static bool HasExistingTerminal()
    {
        if (!Context.IsWorldReady) return false;
        if (Game1.getAllFarmers().Any(farmer => farmer.Items.Any(IsTerminalItem))) return true;
        bool found = false;
        Utility.ForEachLocation(location =>
        {
            foreach (var pair in location.Objects.Pairs)
            {
                if (IsTerminalItem(pair.Value)) { found = true; return false; }
                if (pair.Value is Chest chest && chest.Items.Any(IsTerminalItem))
                { found = true; return false; }
            }
            return !found;
        }, includeInteriors: true, includeGenerated: true);
        return found;
    }

    private static bool IsTerminalItem(Item? item) => item?.QualifiedItemId == QualifiedId;
}
