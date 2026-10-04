# Inventory

`InventoryHud` in save3 is chrome. `I` toggles `OpenGameHudEvent` with an inline HTML panel: a title, the literal string `0 gold`, and a 4×6 grid of empty slots (first three marked `worn`). There is no item definition, no click handler, no pickup, no equip, and no event besides the open/close.

The server is documented to validate inventory. Nothing in save3 gives it an inventory event to validate.

## Model

Server owns the bag. The client owns the panel.

- 3 worn slots, mapped to the cloth the mesh already has: head, body, `hand_r`.
- 21 bag slots. The current HTML grid is 4 columns by 6 rows (24). First three stay worn, the rest are the bag. Do not redesign the panel for v1.
- Gold is an int on the character record, not a slot.
- Item definition is a small table, not a class hierarchy: `itemDefId`, name, max stack, equip bone or -1, damage if it is a weapon.

Mutations happen only on the server. The client publishes an intent. The server publishes `InventoryChangedEvent`. The HUD rewrites one slot. That is the whole loop.

## Events

Proposed. Same publish path as `OpenGameHudEvent`. Ordinary events, not protected, so the other client receives them.

```csharp
public sealed class InventoryChangedEvent
{
    public int EntityId { get; set; }
    public int Slot { get; set; }       // 0..2 worn, 3..23 bag
    public int ItemDefId { get; set; }  // 0 = empty
    public int Count { get; set; }
}

public sealed class ItemPickupRequest
{
    public int EntityId { get; set; }
    public int WorldItemId { get; set; }
    public int ClientTick { get; set; }
}

public sealed class ItemDropRequest
{
    public int EntityId { get; set; }
    public int Slot { get; set; }
    public int Count { get; set; }
    public int ClientTick { get; set; }
}

public sealed class EquipRequest
{
    public int EntityId { get; set; }
    public int FromSlot { get; set; }
    public int ToSlot { get; set; }     // worn slot, bone hand_r for weapons
    public int ClientTick { get; set; }
}
```

`OpenGameHudEvent` stays the open/close. Do not overload it with item data.

## Persistence

Not in `project.json`. Versioned binary next to the account, one sequential write:

- magic, schemaVersion, characterId
- position, yaw, health, gold
- 24 slots, fixed: slotIndex, itemDefId, count, durability

Load on spawn, write on logout and on each accepted mutation. A crash loses at most the last intent.

## HUD

Keep the HTML that is already in `InventoryHud`. Two changes:

- Gold and slot contents come from the last `InventoryChangedEvent` batch, not from a const string.
- Slots need an id so a click can publish `EquipRequest`. The HTML HUD path already used in the example projects supports `data-hook`; give each slot `data-hook="slot-N"`.

The system still subscribes to `KeyInputEvent` and still publishes `OpenGameHudEvent` on `Key.I`. That part is done. Do not rewrite it.

## Equip and the mesh

A weapon equips to `hand_r` (off-hand `hand_l`). The skeleton already has those bones. v1 does not need a new mesh: equipping a weapon sets the worn slot and the combat system reads `hand_r`'s item to pick damage. Showing the weapon is a later attachment on that bone.

Cloth albedo variants 01–03 are kit tints. Equipping a body item can swap the material slot. It must not swap the skeleton.

## Validation

- Pickup only if the world item is within 2m and the bag has a stack or an empty slot.
- Drop only from a slot the character owns. Spawn the world item at the server position, not the client's.
- Equip only if the item def says it fits that slot.
- Reject duplicate ticks inside a short window, same as movement.

## First script seam

Extend `InventoryHud`, do not add a second HUD system. Add the subscribe next to the existing `KeyInputEvent` subscribe:

```csharp
_eventBus.Subscribe<InventoryChangedEvent>(OnInventoryChanged);

private void OnInventoryChanged(InventoryChangedEvent e)
{
    if (e == null) return;
    // Rebuild HtmlContent from the slot table, then Publish OpenGameHudEvent
    // with Open = _open so a live panel refreshes in place.
    Console.WriteLine("[InventoryHud] slot " + e.Slot + " = " + e.ItemDefId + " x" + e.Count);
}
```

Until the server route exists, a debug key can publish a fake `InventoryChangedEvent` so the panel is proven before Citadel is.

## Done when

- `I` still opens the same panel.
- A server-accepted pickup changes one slot on both clients.
- Gold is no longer the literal `0 gold` unless the character record says 0.
- Equip to `hand_r` is what `CombatSystem` reads for damage.
