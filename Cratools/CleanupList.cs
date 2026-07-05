using System.Collections.Generic;

namespace Cratools;

/// <summary>One parsed line from a Teamcraft inventory-cleanup list.</summary>
public readonly record struct CleanupEntry(string Name, int Quantity);

/// <summary>
/// Parses Teamcraft's "inventory cleanup" text. Format is a series of blocks separated by blank
/// lines, each block being an item name line optionally followed by an "xN" quantity line, e.g.:
///
///   Cordial
///   x7
///
///   Grade 6 Dark Matter
///   x52
/// </summary>
public static class CleanupList
{
    public static List<CleanupEntry> Parse(string? text)
    {
        var entries = new List<CleanupEntry>();
        if (string.IsNullOrWhiteSpace(text))
            return entries;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string? pendingName = null;

        void Flush(int qty)
        {
            if (pendingName != null)
            {
                entries.Add(new CleanupEntry(pendingName, qty));
                pendingName = null;
            }
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush(0);
                continue;
            }

            if (TryParseQuantity(line, out var qty))
            {
                Flush(qty); // quantity belongs to the name we just saw
                continue;
            }

            // A new name line: emit any previous name that had no quantity.
            Flush(0);
            pendingName = line;
        }

        Flush(0);
        return entries;
    }

    // Matches "x7", "X7", "x 7", or a bare "7". Item names are never all-digits so this is safe.
    private static bool TryParseQuantity(string line, out int qty)
    {
        qty = 0;
        var s = line;
        if (s.Length > 1 && (s[0] == 'x' || s[0] == 'X'))
            s = s[1..].TrimStart();

        if (s.Length == 0)
            return false;

        foreach (var c in s)
        {
            if (!char.IsDigit(c))
                return false;
        }

        return int.TryParse(s, out qty) && qty > 0;
    }
}
