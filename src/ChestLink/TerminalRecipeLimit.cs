using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace ChestLink;

/// <summary>Enforce one host-crafted terminal for the whole save, shared across farmhands.</summary>
internal static class TerminalRecipeLimit
{
    private const string SaveFlag = "Nooobad.ChestLink/TerminalCrafted";
    private static readonly FieldInfo? RecipeName = typeof(CraftingRecipe).GetField(
        "name", BindingFlags.Instance | BindingFlags.NonPublic);
    private static ModEntry? mod;

    internal static void Initialize(ModEntry entry)
    {
        mod = entry;
        var harmony = new Harmony(entry.ModManifest.UniqueID);
        harmony.Patch(AccessTools.Method(typeof(CraftingRecipe),
                nameof(CraftingRecipe.doesFarmerHaveIngredientsInInventory)),
            prefix: new HarmonyMethod(typeof(TerminalRecipeLimit), nameof(CheckCraftAllowed)));
        harmony.Patch(AccessTools.Method(typeof(CraftingRecipe), nameof(CraftingRecipe.consumeIngredients)),
            postfix: new HarmonyMethod(typeof(TerminalRecipeLimit), nameof(OnCraftCompleted)));
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(CraftingRecipe))
                     .Where(method => method.Name == "getCraftableCount"))
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(TerminalRecipeLimit), nameof(CheckCraftCount)));
    }

    internal static bool IsCrafted => Context.IsWorldReady &&
        Game1.MasterPlayer.modData.TryGetValue(SaveFlag, out string value) && value == "true";

    internal static void MarkCrafted()
    {
        if (!Context.IsWorldReady) return;
        Game1.MasterPlayer.modData[SaveFlag] = "true";
        mod?.BroadcastTerminalCrafted();
    }

    internal static void ReceiveCrafted()
    {
        if (!Context.IsWorldReady) return;
        Game1.MasterPlayer.modData[SaveFlag] = "true";
    }

    internal static void SyncForPeer(long peerId)
    {
        if (IsCrafted) mod?.SendTerminalCrafted(peerId);
    }

    private static bool CheckCraftAllowed(CraftingRecipe __instance, ref bool __result)
    {
        if (!IsTerminalRecipe(__instance) || Context.IsMainPlayer && !IsCrafted) return true;
        __result = false;
        return false;
    }

    private static void OnCraftCompleted(CraftingRecipe __instance)
    {
        if (IsTerminalRecipe(__instance)) MarkCrafted();
    }

    private static bool CheckCraftCount(CraftingRecipe __instance, ref int __result)
    {
        if (!IsTerminalRecipe(__instance) || Context.IsMainPlayer && !IsCrafted) return true;
        __result = 0;
        return false;
    }

    private static bool IsTerminalRecipe(CraftingRecipe recipe) =>
        (string?)RecipeName?.GetValue(recipe) == TerminalContent.RecipeName;
}
