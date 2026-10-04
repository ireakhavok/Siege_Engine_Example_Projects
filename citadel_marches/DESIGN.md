# The Citadel Marches

Design document for the first MMORPG project on RealmFoundry / SiegeEngine (Castle IDE).

Status: proposal, grounded in `Siege_Engine_Example_Projects/save3` as of 2026-10-04.
Nothing in the Combat or Inventory event sections is an existing type. Those names are new `EventBus` events, written to follow `OpenGameHudEvent`.

## What already exists

`save3` (project name `boxsave`, type `2D`, scene `NewTerrain` / `TerrainTest`, mode single player) is the locomotion prototype.

Scene, 13 entities:

- Two `man_mesh_pack` humanoids (ids 1, 2). UE-style skeleton: `pelvis`, `spine_01..03`, `clavicle_*`, `hand_*`, `thigh_*`, `foot_*`.
- Walls from `sm_wall_pack` (ids 3, 7–11).
- Two `greenball_pack` dynamics (ids 4, 5). BodyType 2. Useful as a hit dummy until a creature pack exists.
- One birch (`sm_birch01_pack`), one light, one sound source.
- Terrain heightmap `Assets/Terrain/NewTerrain.tif` + color `NewTerrain.png`. Skybox cubemap under `Assets/Skyboxes`.

Components already on entities:

- `PhysicsComponent`: Position, Rotation, Scale, Velocity, Mass, Health (100 on every entity), BodyType, damping, friction, sleep, `UseBoneHitboxes`, `RagdollSimulationEnabled`, `IsGrounded`, `SlopeLimitDegrees`, `StepHeight`.
- `ModelComponent`: Key, `CastShadows`, `ReceiveShadows`, `HiddenMeshIndices`.

Scripts that compile against `SiegeEngine.dll` + `Foundation.dll` (`Scripts/SiegeScripts.csproj`, net9.0, `OutputPath` `Libs\`):

- `InventoryHud` — `[RegisterGameSystem]`, ctor `(IGameServer, EventBus, InputHandler)`. Subscribes `KeyInputEvent` and `InputHandler.KeyEvent`. `I` press publishes `OpenGameHudEvent` (key `InventoryHud`, inline HTML, bare chrome, docked right, 248×520). The panel is a static 4×6 grid and the string `0 gold`. No item model, no click handlers, no events.
- `TestSlideController` — `[CustomPlayerController] : PlayerMovement`. Calls `base.Update`, then overwrites `player.Physics.Velocity` (ice slide, cap 18) and integrates `player.Physics.Position`. Proof that a custom controller is selected. Not the locomotion we want for the game.

Engine contracts already in use, do not replace them:

- `EventBus.Subscribe` / `Publish`.
- `GameSystem` + `[RegisterGameSystem]`.
- `PlayerMovement` + `[CustomPlayerController]`.
- `ClientPredictionSystem` injected into the player controller; movement goes out through `sendMovementRequest`.
- `ServerValidationSystem` is the documented authority for movement, inventory, and combat (speed and distance caps, frustum / occlusion). save3 does not yet publish anything for it to validate except movement.
- HUD path is HTML/CSS via `OpenGameHudEvent` (`HtmlRelativePath` or `HtmlContent`, `data-hook` style used elsewhere in the example projects).
- Projects are folders: `project.json`, `Scripts/`, `Assets/`, `layout.*.json`. Castle compiles `Scripts/` at Play. Do not commit `Scripts/obj` or `Scripts/Libs`.

## Setting

Name: **The Citadel Marches**.

A low-fantasy siege frontier, matched to the engine vocabulary (Citadel, keeps, RealmFoundry) and to the art already in the scene. No new pack is required for the vertical slice.

The Marches are the open ground in front of a half-built citadel. The heightmap is that ground. The wall meshes already in the scene are the outer works — not decoration, the thing players fight over. Birch stands are the only cover that is not stone. The sky is the existing daytime cubemap.

Players are cloth-armored infantry. The man mesh already has three cloth albedo variants; those are kit tints (levy, sworn, warden), not separate skeletons. Weapons socket to `hand_r` / `hand_l`. There is no creature pack, so the first hostile is the greenball used as a training stake, then a second man mesh driven by the server.

Tone: muddy, practical, short draw distances of meaning. A keep is a claim on a terrain cell, not a castle viewer. Death is a ragdoll (`RagdollSimulationEnabled` is already on the physics component) and a respawn at the last keep, not a cutscene.

Out of scope for the setting until the slice is real: magic, mounts, player housing interiors, day/night cycle, factions with unique meshes.

## What this is, and is not, yet

This is not a live MMO on day one. It is the same project, cloned, with the two missing loops wired through the server that already claims to validate them.

Vertical slice:

1. Walk the terrain on stock `PlayerMovement` with the existing clips (idle, walk, run, left/right run, turn, jump / in-air / land).
2. Click to melee. Client publishes an intent. Server accepts or rejects. On accept, a short bone-hitbox window damages a target. Health is `PhysicsComponent.Health`. At 0, ragdoll.
3. `I` still opens the panel, but slots are filled from a server bag. Equip and move are intents. The panel rewrites itself from a delta event.
4. A second player on local Citadel, so prediction and the entity delta tracker are actually exercised.

Bow and any other projectile are phase 2, and they are a server ray (or a server-owned body), never a client-authoritative projectile. save3 has no projectile system.

## Clone

Source: `Siege_Engine_Example_Projects/save3`.
Target: sibling folder `citadel_marches` in the same repo (or a new repo if the asset weight should not sit next to bowling).

Copy:

- `Assets/` (man mesh, terrain, skybox, wall, birch, greenball).
- `Scripts/SiegeScripts.csproj`, `Scripts/InventoryHud.cs`, `Scripts/InventoryHud.html`.
- `project.json`, `layout.*.json`.

Do not copy:

- `Scripts/obj`, `Scripts/Libs` (Castle fills these at Play; the csproj hint paths are machine-local).
- `TestSlideController.cs` (keep it in save3 as the controller proof).

After copy, in `project.json`:

- `Name` → `citadel_marches`.
- Leave mode on local / single player until movement validation is proven, then local Citadel. Do not flip to dedicated Steam on the first commit.

Player state does not live in the project tree. A versioned binary per character, outside the repo, for example `realm-data/accounts/<id>/character.bin`:

- header: magic u32, schemaVersion u16, characterId u64
- pos xyz, yaw, health, gold
- fixed slot array: slotIndex u16, itemDefId u32, count u16, durability u16

One sequential write. No JSON on the hot path.

## Systems map

```
input (KeyInputEvent, mouse)
    → client intent event          never trusted
    → ServerValidationSystem       range, cooldown, speed, occlusion
    → resolved event               networkSync, not ProtectedEvent
    → GameSystem on each client    animation, HUD, hit flash
```

`ProtectedEvent` is rejected from mods and clients. Game events that clients must receive are ordinary events.

Proposed intents (new types, same publish style as `OpenGameHudEvent`):

| Intent | Resolved |
| --- | --- |
| `AttackRequest { AttackerId, AimDir, AttackId, ClientTick }` | `AttackResolvedEvent { HitEntityId, Damage, HitBone }` |
| `ItemPickupRequest` / `ItemDropRequest` | `InventoryChangedEvent { EntityId, Slot, ItemDefId, Count }` |
| `EquipRequest` (target bone `hand_r` / `hand_l`) | `InventoryChangedEvent` plus a worn-slot change |

Replication stays on the existing entity layout. Dense `EntityId` with a free list, Structure-of-Arrays snapshot for position, velocity, grounded, health, cell id. Do not allocate a managed object graph per remote player. Health stays on `PhysicsComponent` until a `VitalComponent` is actually needed.

## Rendering budget (v1)

The man mesh maps are uncompressed TGA, about 50MB each (albedo, normal, AO, metallic), plus three cloth albedos. That is fine for one local character and fatal once many players exist.

- Compress to BC7 with mips (BC4 or BC7 for metallic). Stream mips by distance. Cloth variants are material slots, not extra skeletons.
- Shadows: `CastShadows` / `ReceiveShadows` are already set. Two cascades. Only the nearest casters (cap around 16) go into the player cascade. Terrain can use a cheap cascade or baked AO.
- Animation LOD: full blend near, single clip mid, impostor far. Mesh LODs are not in the pack yet; author two later.
- Hit feedback: short emissive on the hit material, additive unlit flash for the swing. Damage numbers can go through the existing HTML HUD. No extra dynamic light per hit.
- Death uses the ragdoll flag already on the physics component.

## Order of work

1. Clone the folder. Confirm Play still loads the man mesh and terrain with stock movement.
2. `CombatSystem` as `[RegisterGameSystem]`. Mouse click publishes `AttackRequest`. Server validates. Greenball is the dummy. Health decrements. Zero enables ragdoll.
3. Replace the static inventory HTML with a slot model driven by `InventoryChangedEvent`. `I` stays.
4. Second client on local Citadel. Confirm prediction does not fight the attack resolve.
5. Only then: bow as a server ray, keep claim on a terrain cell, texture compression.

See `COMBAT.md` and `INVENTORY.md` for the event contracts and the first scripts.
