using Netcode;
using StardewValley;

namespace ChestLink;

/// <summary>Use the game's NetFields format so equipment and mod data survive a host-to-client transfer.</summary>
internal static class ItemNetCodec
{
    internal static string Encode(Item item) =>
        Convert.ToBase64String(Game1.Multiplayer.writeObjectFullBytes(new NetRoot<Item>(item), null));

    internal static Item Decode(string data)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(data));
        using var reader = new BinaryReader(stream);
        return Game1.Multiplayer.readObjectFull<Item>(reader).Value ??
            throw new InvalidDataException("Item payload was empty.");
    }
}
