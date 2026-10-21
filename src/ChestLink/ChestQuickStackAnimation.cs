using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using System.Reflection;

namespace ChestLink;

/// <summary>Animate each destination chest as its flying item arrives.</summary>
internal static class ChestQuickStackAnimation
{
    private const int FrameMs = 65;
    private static readonly List<PendingChest> Pending = new();
    private static readonly FieldInfo? CurrentLidFrame = typeof(Chest).GetField(
        "currentLidFrame", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void Play(StoragePulse pulse)
    {
        long now = Environment.TickCount64;
        foreach (StoragePulseChest target in pulse.Chests)
            Pending.Add(new PendingChest(target, now + target.OpenDelayMs,
                now + target.CloseDelayMs));
    }

    internal static void Update()
    {
        if (!StardewModdingAPI.Context.IsWorldReady) { Pending.Clear(); return; }
        long now = Environment.TickCount64;
        GameLocation? location = Game1.currentLocation;
        for (int i = Pending.Count - 1; i >= 0; i--)
        {
            PendingChest animation = Pending[i];
            if (now < animation.OpenAt) continue;
            if (location is null || animation.Target.LocationName != location.NameOrUniqueName)
            {
                if (now >= animation.CloseAt + 400) Pending.RemoveAt(i);
                continue;
            }
            if (!location.Objects.TryGetValue(new Vector2(animation.Target.X,
                    animation.Target.Y), out var obj) ||
                obj is not Chest chest || !ChestCatalog.IsOrdinary(chest))
            {
                Pending.RemoveAt(i);
                continue;
            }

            int first = chest.startingLidFrame.Value;
            int last = chest.getLastLidFrame();
            int frameCount = Math.Max(0, last - first);
            long openDuration = frameCount * FrameMs;
            if (!animation.Started)
            {
                animation.Started = true;
                chest.shakeTimer = Math.Max(chest.shakeTimer, 180);
                Game1.playSound("openChest");
            }
            if (now < animation.CloseAt)
                CurrentLidFrame?.SetValue(chest, first + (int)Math.Min(frameCount,
                    (now - animation.OpenAt) / FrameMs));
            else if (now < animation.CloseAt + openDuration)
                CurrentLidFrame?.SetValue(chest, last - (int)Math.Min(frameCount,
                    (now - animation.CloseAt) / FrameMs));
            else
            {
                chest.resetLidFrame();
                Pending.RemoveAt(i);
            }
        }
    }

    private sealed class PendingChest(StoragePulseChest target, long openAt, long closeAt)
    {
        internal StoragePulseChest Target { get; } = target;
        internal long OpenAt { get; } = openAt;
        internal long CloseAt { get; } = closeAt;
        internal bool Started { get; set; }
    }
}
