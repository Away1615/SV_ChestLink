using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace ChestLink;

/// <summary>
/// Show each successfully deposited item flying from the farmer to its actual chest.
/// This uses the game's temporary sprites, like Convenient Inventory's quick stack
/// effect, without depending on that mod being installed.
/// </summary>
internal static class QuickStackFlightAnimation
{
    // Temporary animated sprites are synchronized to every nearby player. Keep
    // large quick-stack batches from flooding the multiplayer update queue.
    internal const int MaxAnimatedTransfers = 12;

    internal static StoragePulse Play(Farmer player, IReadOnlyList<(ChestSource Chest, Item Item)> transfers)
    {
        GameLocation? origin = player.currentLocation;
        var pulse = new StoragePulse();
        if (origin is null) return pulse;

        var sprites = new Dictionary<GameLocation, TemporaryAnimatedSpriteList>();
        var chestTimings = new Dictionary<ChestSource, StoragePulseChest>();
        Vector2 start = player.Position + new Vector2(16f, -20f);
        int index = 0;
        foreach ((ChestSource chest, Item item) in transfers.Take(MaxAnimatedTransfers))
        {
            if (chest.Location.NameOrUniqueName != origin.NameOrUniqueName) continue;
            ParsedItemData? data = ItemRegistry.GetData(item.QualifiedItemId);
            if (data is null) continue;

            int delay = Math.Min(index++ * 55, 550);
            Vector2 end = chest.Tile * Game1.tileSize + new Vector2(8f, -8f);
            Vector2 landingStart = end - new Vector2(0f, 24f);
            int arrival;
            int flightMs = Duration(start, landingStart);
            Add(sprites, chest.Location, Create(data, start, landingStart, delay, flightMs));
            arrival = delay + flightMs;
            Add(sprites, chest.Location, CreateLanding(data, landingStart, end, arrival));
            // The farmhouse fridge has a tile but no placed chest lid to animate.
            if (!chest.Location.Objects.TryGetValue(chest.Tile, out var placed) ||
                !ReferenceEquals(placed, chest.Chest)) continue;
            if (!chestTimings.TryGetValue(chest, out StoragePulseChest? timing))
            {
                timing = new StoragePulseChest
                {
                    LocationName = chest.Location.NameOrUniqueName,
                    X = (int)chest.Tile.X,
                    Y = (int)chest.Tile.Y,
                    OpenDelayMs = Math.Max(0, arrival - 160),
                    CloseDelayMs = arrival + 420
                };
                chestTimings.Add(chest, timing);
            }
            else
            {
                timing.OpenDelayMs = Math.Min(timing.OpenDelayMs, Math.Max(0, arrival - 160));
                timing.CloseDelayMs = Math.Max(timing.CloseDelayMs, arrival + 420);
            }
        }

        foreach ((GameLocation location, TemporaryAnimatedSpriteList batch) in sprites)
            Game1.Multiplayer.broadcastSprites(location, batch);
        pulse.Chests = chestTimings.Values.ToList();
        return pulse;
    }

    private static TemporaryAnimatedSprite Create(ParsedItemData data, Vector2 start,
        Vector2 end, int delay, int duration)
    {
        float distance = Vector2.Distance(start, end);
        float arc = Math.Clamp(distance * 0.18f, 40f, 120f);
        Vector2 velocity = (end - start) / duration + new Vector2(0f, -4f * arc / duration);
        var sprite = new TemporaryAnimatedSprite(data.TextureName, data.GetSourceRect(),
            start, false, 0f, Color.White)
        {
            scale = 4f,
            interval = duration,
            totalNumberOfLoops = 0,
            delayBeforeAnimationStart = delay,
            timeBasedMotion = true,
            motion = velocity,
            acceleration = new Vector2(0f, 8f * arc / (duration * duration)),
            layerDepth = 1f
        };
        return sprite;
    }

    private static int Duration(Vector2 start, Vector2 end) =>
        (int)Math.Clamp(500f + 10f * MathF.Sqrt(Vector2.Distance(start, end)), 500f, 1100f);

    private static TemporaryAnimatedSprite CreateLanding(ParsedItemData data, Vector2 start,
        Vector2 end, int delay) => new(data.TextureName, data.GetSourceRect(), start,
            false, 0f, Color.White)
        {
            scale = 4f,
            interval = 160f,
            totalNumberOfLoops = 0,
            delayBeforeAnimationStart = delay,
            timeBasedMotion = true,
            motion = (end - start) / 160f,
            layerDepth = 1f
        };

    private static void Add(Dictionary<GameLocation, TemporaryAnimatedSpriteList> batches,
        GameLocation location, TemporaryAnimatedSprite sprite)
    {
        if (!batches.TryGetValue(location, out TemporaryAnimatedSpriteList? batch))
        {
            batch = new TemporaryAnimatedSpriteList();
            batches.Add(location, batch);
        }
        batch.Add(sprite);
    }
}
