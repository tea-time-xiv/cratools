using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace Cratools.Armory;

/// <summary>One piece's place in one outfit set: which set, and which of the eleven set slots.</summary>
public readonly record struct SetMembership(uint SetId, int SlotIndex);

/// <summary>An outfit set as the game defines it: a token item plus up to eleven pieces.</summary>
public sealed class OutfitSet
{
    public uint SetId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Piece item ids indexed by set slot; 0 where the set leaves a slot empty.</summary>
    public uint[] Pieces { get; init; } = new uint[GlamourSets.SetSlotCount];

    public int PieceCount { get; init; }
}

/// <summary>
/// Static, sheet-derived knowledge about outfit glamours ("store as set", patch 7.1) and the
/// armoire, built once at load in the same style as <see cref="EquipRules"/>.
///
/// Two sheets carry it:
///
///  - <c>MirageStoreSetItem</c>: one row per outfit. The row id is the set's *token* item — the
///    "… Attire" entry that occupies a single glamour dresser slot — and the eleven columns are the
///    pieces, in the fixed order <see cref="SetSlotNames"/>. That column index is the same slot
///    index the game uses for its eleven-bit set masks, so it is kept, not discarded.
///  - <c>Cabinet</c>: item id to cabinet id. Armoire-eligible gear cannot be put in the dresser at
///    all, so this sheet is what stops the plugin from suggesting an impossible deposit.
///
/// <c>MirageStoreSetItemLookup</c> (piece id to set ids) is deliberately not used: walking
/// MirageStoreSetItem once yields the same reverse map *and* the slot index, from one sheet.
/// </summary>
public sealed class GlamourSets
{
    public const int SetSlotCount = 11;

    public static readonly string[] SetSlotNames =
    {
        "Main Hand", "Off Hand", "Head", "Body", "Hands", "Legs", "Feet",
        "Earrings", "Necklace", "Bracelets", "Ring",
    };

    private readonly Dictionary<uint, OutfitSet> sets = new();
    private readonly Dictionary<uint, List<SetMembership>> membershipsByPiece = new();
    private readonly Dictionary<uint, uint> cabinetIdByItem = new();

    public GlamourSets(IDataManager dataManager, IPluginLog log)
    {
        var setSheet = dataManager.GetExcelSheet<MirageStoreSetItem>();
        var items = dataManager.GetExcelSheet<Item>();
        var cabinet = dataManager.GetExcelSheet<Cabinet>();

        if (setSheet == null || items == null)
        {
            log.Error("Cratools: could not open the MirageStoreSetItem/Item sheets; outfit sets are empty.");
            return;
        }

        foreach (var row in setSheet)
        {
            if (row.RowId == 0)
                continue;

            var pieces = new uint[SetSlotCount];
            pieces[0] = row.MainHand.RowId;
            pieces[1] = row.OffHand.RowId;
            pieces[2] = row.Head.RowId;
            pieces[3] = row.Body.RowId;
            pieces[4] = row.Hands.RowId;
            pieces[5] = row.Legs.RowId;
            pieces[6] = row.Feet.RowId;
            pieces[7] = row.Earrings.RowId;
            pieces[8] = row.Necklace.RowId;
            pieces[9] = row.Bracelets.RowId;
            pieces[10] = row.Ring.RowId;

            var count = 0;
            foreach (var piece in pieces)
            {
                if (piece != 0)
                    count++;
            }

            // Roughly eighty rows are placeholders with no pieces at all.
            if (count == 0)
                continue;

            var name = items.GetRowOrDefault(row.RowId)?.Name.ExtractText() ?? $"Set #{row.RowId}";
            sets[row.RowId] = new OutfitSet
            {
                SetId = row.RowId,
                Name = name,
                Pieces = pieces,
                PieceCount = count,
            };

            for (var slot = 0; slot < SetSlotCount; slot++)
            {
                var piece = pieces[slot];
                if (piece == 0)
                    continue;

                if (!membershipsByPiece.TryGetValue(piece, out var list))
                    membershipsByPiece[piece] = list = new List<SetMembership>(1);

                list.Add(new SetMembership(row.RowId, slot));
            }
        }

        if (cabinet != null)
        {
            foreach (var row in cabinet)
            {
                if (row.Item.RowId != 0)
                    cabinetIdByItem[row.Item.RowId] = row.RowId;
            }
        }
        else
        {
            log.Warning("Cratools: could not open the Cabinet sheet; armoire gear may be mislabelled.");
        }

        log.Information($"Cratools: indexed {sets.Count} outfit sets over {membershipsByPiece.Count} " +
                        $"pieces, and {cabinetIdByItem.Count} armoire items.");
    }

    public int SetCount => sets.Count;

    public int PieceCount => membershipsByPiece.Count;

    public int CabinetItemCount => cabinetIdByItem.Count;

    /// <summary>True when the id is an outfit token — what a stored set occupies a dresser slot as.</summary>
    public bool IsSetToken(uint itemId) => sets.ContainsKey(itemId);

    public bool TryGetSet(uint setId, out OutfitSet set) => sets.TryGetValue(setId, out set!);

    public string SetName(uint setId) => sets.TryGetValue(setId, out var set) ? set.Name : $"Set #{setId}";

    /// <summary>Every outfit the piece belongs to. A few hundred pieces belong to more than one.</summary>
    public bool TryGetMemberships(uint itemId, out IReadOnlyList<SetMembership> memberships)
    {
        if (membershipsByPiece.TryGetValue(itemId, out var list))
        {
            memberships = list;
            return true;
        }

        memberships = System.Array.Empty<SetMembership>();
        return false;
    }

    /// <summary>Armoire-eligible gear. The dresser refuses these outright, so they are never a gap.</summary>
    public bool IsCabinetItem(uint itemId) => cabinetIdByItem.ContainsKey(itemId);

    public IReadOnlyDictionary<uint, uint> CabinetIds => cabinetIdByItem;
}
