using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Cratools.Armory;

/// <summary>Where the dresser contents were read from, for the UI to report honestly.</summary>
public enum DresserSource
{
    /// <summary>Nothing readable: the plugin cannot tell what is stored.</summary>
    None,

    /// <summary>ItemFinderModule's saved cache. Survives zone changes and logouts.</summary>
    ItemFinderCache,

    /// <summary>MirageManager, live. Only populated while the dresser has been opened this zone.</summary>
    PrismBox,
}

/// <summary>
/// A read-only snapshot of what the glamour dresser and the armoire already hold.
///
/// Two sources, in preference order:
///
///  1. <see cref="MirageManager"/> — the live prism box. Authoritative, because
///     <c>IsSetSlotUnlocked</c> answers the per-slot question directly, but it is only populated
///     while the dresser has been opened in the current zone.
///  2. <see cref="ItemFinderModule"/> — the cache behind the item search's "you already have this"
///     marker. A saved user file keyed to the character, so unlike MirageManager it survives zone
///     changes and logouts. This is why no snapshot of our own has to be persisted in the
///     configuration.
///
/// Two things about the cache, both established by "/cratools glamourdump" on 2026-09-05:
///
///  - <c>IsGlamourDresserCached</c> is *not* a "has data" flag: it read false on a character whose
///     eight hundred entries were fully populated. It is reported, never gated on.
///  - The parallel <c>GlamourDresserItemSetUnlockBits</c> mask marks *missing* slots, not filled
///     ones, see <see cref="SetBitsMeanMissingSlots"/>.
///
/// A stored outfit occupies one dresser entry holding the set's *token* id (the "… Attire" item),
/// not its pieces; the eleven-bit mask covers which of the set's slots are filled. The same token
/// can appear at several entries when the player stored an outfit more than once, so the per-slot
/// answers are OR-ed together per set.
/// </summary>
public sealed unsafe class DresserState
{
    private readonly HashSet<uint> looseItems = new();
    private readonly Dictionary<uint, ushort> setSlotBits = new();
    private readonly HashSet<uint> armoireItems = new();

    public DresserSource Source { get; private set; } = DresserSource.None;

    public bool DresserKnown => Source != DresserSource.None;

    public bool ArmoireKnown { get; private set; }

    /// <summary>Non-empty dresser entries seen, tokens included.</summary>
    public int DresserEntryCount { get; private set; }

    public int LooseItemCount => looseItems.Count;

    public int StoredSetCount => setSlotBits.Count;

    public int ArmoireItemCount => armoireItems.Count;

    /// <summary>The item sits in the dresser on its own, outside any outfit.</summary>
    public bool IsLooseInDresser(uint itemId) => looseItems.Contains(itemId);

    /// <summary>The outfit is stored, whether or not this particular slot of it is filled.</summary>
    public bool IsSetStored(uint setId) => setSlotBits.ContainsKey(setId);

    /// <summary>The piece occupying <paramref name="slotIndex"/> of the outfit is already stored.</summary>
    public bool IsSetSlotFilled(uint setId, int slotIndex)
        => setSlotBits.TryGetValue(setId, out var bits) && (bits & (1 << slotIndex)) != 0;

    public ushort SetSlotBits(uint setId) => setSlotBits.TryGetValue(setId, out var bits) ? bits : (ushort)0;

    public bool IsInArmoire(uint itemId) => armoireItems.Contains(itemId);

    /// <summary>Reads both stores. Never writes game memory.</summary>
    public static DresserState Capture(GlamourSets sets)
    {
        var state = new DresserState();
        state.ReadDresser(sets);
        state.ReadArmoire(sets);
        return state;
    }

    private void ReadDresser(GlamourSets sets)
    {
        if (ReadPrismBox(sets))
        {
            Source = DresserSource.PrismBox;
            return;
        }

        if (ReadItemFinderCache(sets))
            Source = DresserSource.ItemFinderCache;
    }

    /// <summary>Turns a mask of missing slots into one of filled slots, over the set's own slots.</summary>
    private static ushort FilledFromMissing(GlamourSets sets, uint setId, ushort missing)
    {
        if (!sets.TryGetSet(setId, out var set))
            return 0;

        ushort filled = 0;
        for (var slot = 0; slot < GlamourSets.SetSlotCount; slot++)
        {
            if (set.Pieces[slot] != 0 && (missing & (1 << slot)) == 0)
                filled |= (ushort)(1 << slot);
        }

        return filled;
    }

    /// <summary>
    /// How to read a bit in <c>GlamourDresserItemSetUnlockBits</c>: set means the outfit slot is
    /// *not* stored. Despite the name, the mask is the complement of what
    /// <see cref="MirageManager.IsSetSlotUnlocked"/> reports.
    ///
    /// Confirmed in-game on 2026-09-05 with the dresser open: over eight stored outfits, reading
    /// the bits as missing slots matched IsSetSlotUnlocked 8/8, reading them as filled slots 0/8.
    /// Six of the eight carried an all-zero mask and were complete outfits.
    ///
    /// Flip this one constant if the prism-box cross-check in <see cref="GlamourDebug"/> ever
    /// disagrees; nothing else depends on the polarity.
    /// </summary>
    public const bool SetBitsMeanMissingSlots = true;

    private bool ReadItemFinderCache(GlamourSets sets)
    {
        var finder = ItemFinderModule.Instance();
        if (finder == null)
            return false;

        var ids = finder->GlamourDresserItemIds;
        var bits = finder->GlamourDresserItemSetUnlockBits;

        for (var i = 0; i < ids.Length; i++)
        {
            var id = GearsetIndex.Normalize(ids[i]);
            if (id == 0)
                continue;

            DresserEntryCount++;

            if (!sets.IsSetToken(id))
            {
                looseItems.Add(id);
                continue;
            }

            var mask = i < bits.Length ? bits[i] : (ushort)0;
            Merge(id, SetBitsMeanMissingSlots ? FilledFromMissing(sets, id, mask) : mask);
        }

        return DresserEntryCount > 0;
    }

    private bool ReadPrismBox(GlamourSets sets)
    {
        var mirage = MirageManager.Instance();
        if (mirage == null || !mirage->PrismBoxLoaded)
            return false;

        var ids = mirage->PrismBoxItemIds;

        for (var i = 0; i < ids.Length; i++)
        {
            var id = GearsetIndex.Normalize(ids[i]);
            if (id == 0)
                continue;

            DresserEntryCount++;

            if (!sets.IsSetToken(id))
            {
                looseItems.Add(id);
                continue;
            }

            ushort mask = 0;
            for (var slot = 0; slot < GlamourSets.SetSlotCount; slot++)
            {
                if (mirage->IsSetSlotUnlocked((uint)i, slot))
                    mask |= (ushort)(1 << slot);
            }

            Merge(id, mask);
        }

        return true;
    }

    private void Merge(uint setId, ushort mask)
    {
        setSlotBits[setId] = setSlotBits.TryGetValue(setId, out var existing)
            ? (ushort)(existing | mask)
            : mask;
    }

    private void ReadArmoire(GlamourSets sets)
    {
        var uiState = UIState.Instance();
        if (uiState == null || !uiState->Cabinet.IsCabinetLoaded())
            return;

        ArmoireKnown = true;

        foreach (var (itemId, cabinetId) in sets.CabinetIds)
        {
            if (uiState->Cabinet.IsItemInCabinet(cabinetId))
                armoireItems.Add(itemId);
        }
    }
}
