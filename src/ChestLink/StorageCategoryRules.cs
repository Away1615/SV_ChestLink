namespace ChestLink;

/// <summary>Pure category rules for the storage view; never changes item data.</summary>
internal static class StorageCategoryRules
{
    internal static string Classify(string qualifiedId, string? objectType, int category)
    {
        string? byId = qualifiedId switch
        {
            "(O)417" => "farming",
            "(O)430" or "(O)178" or "(O)107" => "animal",
            "(O)152" or "(O)153" or "(O)157" or
                "(O)SeaJelly" or "(O)RiverJelly" or "(O)CaveJelly" => "fishing",
            "(O)535" or "(O)536" or "(O)537" or "(O)749" or "(O)275" => "minerals",
            "(O)245" or "(O)246" or "(O)247" or "(O)419" or "(O)423" => "cooking",
            "(O)395" => "artisan",
            "(O)413" or "(O)437" or "(O)439" or "(O)680" or "(O)857" => "monster",
            // Existing specific material exceptions are retained.
            "(O)388" or "(O)709" or "(O)771" or "(O)390" or "(O)330" or
                "(O)382" or "(O)334" or "(O)335" or "(O)336" or
                "(O)337" or "(O)338" => "resources",
            _ => null
        };
        if (byId is not null) return byId;

        // Data/Objects Type applies only to object items, not display names or tags.
        if (qualifiedId.StartsWith("(O)", StringComparison.Ordinal))
        {
            if (objectType == "Arch") return "minerals";
            if (objectType == "Ring") return "equipment";
        }

        string? byCategory = category switch
        {
            -74 or -75 or -79 or -80 or -19 => "farming",
            -5 or -6 or -14 or -18 => "animal",
            -4 or -21 or -22 or -23 => "fishing",
            -81 => "forage",
            -15 or -16 => "resources",
            -2 or -12 => "minerals",
            -28 => "monster",
            -7 or -25 => "cooking",
            -26 or -27 => "artisan",
            -8 or -9 or -24 => "manufactured",
            -29 or -96 or -98 or -99 or -101 => "equipment",
            -95 or -97 or -100 => "clothing",
            -17 or -20 or -102 or -103 => "other",
            _ => null
        };
        if (byCategory is not null) return byCategory;

        // Non-object types can have no meaningful Object.Category value.
        if (qualifiedId.StartsWith("(H)", StringComparison.Ordinal) ||
            qualifiedId.StartsWith("(S)", StringComparison.Ordinal) ||
            qualifiedId.StartsWith("(P)", StringComparison.Ordinal) ||
            qualifiedId.StartsWith("(B)", StringComparison.Ordinal)) return "clothing";
        if (qualifiedId.StartsWith("(T)", StringComparison.Ordinal) ||
            qualifiedId.StartsWith("(W)", StringComparison.Ordinal) ||
            qualifiedId.StartsWith("(R)", StringComparison.Ordinal) ||
            qualifiedId.StartsWith("(TR)", StringComparison.Ordinal)) return "equipment";
        if (qualifiedId.StartsWith("(BC)", StringComparison.Ordinal) ||
            qualifiedId.StartsWith("(WP)", StringComparison.Ordinal) ||
            qualifiedId.StartsWith("(FL)", StringComparison.Ordinal)) return "manufactured";
        return "other";
    }
}
