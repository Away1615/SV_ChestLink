using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace ChestLink;

/// <summary>A small pixel padlock drawn with the game's solid-color texture.</summary>
internal static class LockIcon
{
    internal static void Draw(SpriteBatch batch, Rectangle slot)
    {
        int x = slot.X - 4;
        int y = slot.Y - 4;
        Color outline = new(83, 45, 28);
        Color metal = new(221, 196, 137);
        Color body = new(228, 157, 52);
        Color highlight = new(255, 218, 112);
        Fill(batch, x + 6, y, 12, 4, outline);
        Fill(batch, x + 4, y + 4, 4, 10, outline);
        Fill(batch, x + 16, y + 4, 4, 10, outline);
        Fill(batch, x + 8, y + 4, 8, 8, metal);
        Fill(batch, x + 2, y + 12, 20, 14, outline);
        Fill(batch, x + 4, y + 14, 16, 10, body);
        Fill(batch, x + 4, y + 14, 16, 2, highlight);
        Fill(batch, x + 10, y + 18, 4, 4, outline);
    }

    private static void Fill(SpriteBatch batch, int x, int y, int width, int height, Color color) =>
        batch.Draw(Game1.staminaRect, new Rectangle(x, y, width, height), color);
}
