# Clone save3 → citadel_marches

Do this in Castle, or as a folder copy beside `save3`. Do not copy build output.

## Keep

- `Assets/man_mesh_pack` and the other packs the scene references (`sm_wall_pack`, `greenball_pack`, `sm_birch01_pack`)
- `Assets/Terrain`, `Assets/Skyboxes`
- `Scripts/SiegeScripts.csproj`
- `Scripts/InventoryHud.cs`, `Scripts/InventoryHud.html`
- `project.json`, `layout.Animator.json`, `layout.Runtime.json`, `layout.Scene Editor.json`, `layout.Terrain.json`, `layout.Workshop.json`

## Drop

- `Scripts/obj`
- `Scripts/Libs` (hint paths in the csproj are machine-local; Castle refills them at Play)
- `Scripts/TestSlideController.cs` (leave it in save3)

## Retitle

In `project.json` set the project name to `citadel_marches`. Leave the scene on `NewTerrain`. Leave mode local until a second client has validated movement.

## First files to add

- `Scripts/CombatSystem.cs` from `COMBAT.md`
- The `InventoryChangedEvent` subscribe from `INVENTORY.md`, inside the existing `InventoryHud`

## Prove the clone before adding systems

Play. The man mesh should idle on the heightmap, walls and the greenball present, `I` opening the empty inventory. If that fails, the clone is wrong and combat work will hide it.
