# Ashfen

A single-player courtyard fight on a new terrain level. Three hostiles, a blade, and a draught. The walking player is the built-in controller, not a custom scene.

This folder does not replace `RuntimeGameplay` and it does not register a custom scene. `sceneType` is `TerrainTest` and `customSceneClass` is null. `controllerTypeName` is `PlayerController`. Attack and the bag stay in `Scripts/AshfenLoop.cs`.

## Open it in Castle

Copy this folder to the directory Castle loads projects from, next to the other projects:

`Documents/CastleBuilder/Projects/Ashfen`

Open that folder in Castle. `LastOpenedScene` is `AshfenCourtyard`. Before Play, delete a stale script build if one is left over:

- `Ashfen/Scripts/Libs/`
- `Foundation/.../RuntimeTemp/SiegeScripts.dll`

Then Play. Castle compiles `Scripts/SiegeScripts.csproj` against `Scripts/Libs/SiegeEngine.dll` and `Foundation.dll`. Do not patch `SceneManager`.

## What is new

The scene is `AshfenCourtyard`, not save3's `NewTerrain`.

- `Assets/Terrain/Ashfen.tif` is a new 205×205 32-bit float heightmap (uncompressed TIFF, sample format IEEE float). It is a ruin valley: raised map edges, a flat courtyard, a lower packed-earth path coming in from the south, and a few hills. It is not save3's `NewTerrain.tif`.
- `Assets/Terrain/Ashfen.png` is a new 4096×4096 color texture (stone courtyard, packed-earth path, grass). `project.json` stores that path with a backslash, the same way save3 stored its color texture. `normalTexturePath`, `splatMapPath`, and `embeddedHeightmapData` are null.
- `Assets/Skyboxes/Ashfen/` is a new dusk cubemap (`xpos`, `xneg`, `ypos`, `yneg`, `zpos`, `zneg`). Skybox `enabled` is true, `type` is `Cubemap`, `cubemapPath` is empty.

## What is linked, not new

Meshes are the asset packs already in this folder (the same packs as `ireakhavok/Siege_Engine_Example_Projects`):

- `man_mesh_pack` — player spawn marker, entity 2
- `sm_wall_pack` — ruined courtyard ring
- `sm_birch01_pack` — birch trees outside the walls
- `greenball_pack` — three hostile markers (entities 21, 22, 23) and two pickup markers (31 blade, 32 draught)
- `blend_pack_all` — animation pack on the scene, not a placed mesh

`CameraType` is `AngledOrtho`. Scene `cameraMode` is `ThirdPerson`. `avatarPackKey` is `man_mesh_pack`. `animationPackKey` is `blend_pack_all`. `preferredSpawnPointIds` is `[2]`. Entity 2 sits on the courtyard floor at about (108, 88, 6.37).

Clockwork, pool, and checkers do not ship pack folders that this level can place. Their meshes are not copied here.

## Keys

- W A S D move, mouse looks. That is stock `PlayerMovement` behind `PlayerController`.
- F strikes the nearest living hostile within 2.4 metres on the ground plane. Cooldown 0.45 seconds. Bare hands hit for 12. The blade raises that to 28.
- I opens and closes the bag.

Walk into a pickup. The blade and the draught are added with `InventoryComponent.AddItem`, then `ItemPickedUpEvent` and `ValidateInventory(entityId, "AddItem", itemId)`. The draught heals 45, capped at 100. Neither pickup revives you.

The hostiles and pickups are entities in `project.json`. The script does not spawn a second set. It still treats type `AshfenHostile` / `AshfenPickup:` as combat targets, and it also recognizes the placed marker ids above. Walls and trees are level geometry only.

## Win, lose

Defeat all three hostiles. The HUD counts them as `Courtyard n / 3` and, at 3, says the courtyard is quiet.

Hostiles chase inside 9 metres and deal 8 contact damage every 0.85 seconds. You have 100 HP, stored on the player `PhysicsComponent.Health`. At 0 you are dead: you stop dealing and taking damage, and the HUD says so. Walking is still the stock controller.

## HUD

`OpenGameHudEvent.HtmlContent` is what the panel shows. The HTML files next to the script are the same layout, not a live data source.

This project was not compiled here. There is no `SiegeEngine.dll` in the folder.
