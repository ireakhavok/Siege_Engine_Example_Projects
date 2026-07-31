# Chess — install (read fully)

## A. Engine fix (required — removes man_mesh / default Player)

Copy / merge:

`engine-template/ENGINE_PATCH_SceneManager.cs`

→ `SiegeEngine/Core/Managers/SceneManager.cs`

What it removes from **both** `OnSwitchScene` and `SwitchToRuntimeGameplay`:

- `LoadModel(.../Characters/Man_Mesh.fbx)`
- `new Player(...)` + `man_mesh` ModelComponent
- forced `PlayerMovement` when there is no player

Rebuild **Foundation / SiegeEngine**, then run the IDE again.

Without this patch, Play will always log man_mesh noise. That is engine code, not chess.

---

## B. Project scripts

Copy into `Documents/CastleBuilder/Projects/chess/Scripts/Chess/`:

| File | Role |
|------|------|
| `ChessBoardState.cs` | board data |
| `ChessRules.cs` | legal moves |
| `ChessAI.cs` | minimax AI |
| `ChessScene.cs` | draws board (`ChessScene(SceneContext)`) |
| `ChessLaunchSystem.cs` | `ChessRuntimeHook` rebinds `RuntimeGameplay` → chess |

Delete stale DLLs:

```
Projects/chess/Scripts/Libs/
Foundation/.../RuntimeTemp/SiegeScripts.dll
```

---

## C. Play — expected log

```
Build succeeded. Exit: 0
[ScriptLoader] Discovered [RegisterGameSystem]: ChessProject.ChessRuntimeHook
[ScriptLoader] Discovered [CustomSceneEntry]: ChessProject.ChessScene
[ScriptLoader] Registered custom GameSystem: ChessRuntimeHook
[ChessProject] Bound RuntimeGameplay → ChessScene (no SwitchScene)
[ChessProject] Create('RuntimeGameplay') → new ChessScene(ctx)
[ChessScene] Constructed via SceneContext
[ChessScene] Ready — click white piece…
```

**No** FBX man_mesh dump. **No** `No satisfiable constructor`. Greenish clear + 8×8 board.

---

## What went wrong last run

```
error CS0246: RegisterGameSystemAttribute could not be found
Build FAILED. Exit: 1
```

`ChessLaunchSystem.cs` was missing `using SiegeEngine.Core.Managers;`.  
Build failed → pure client loaded **old** `RuntimeTemp\SiegeScripts.dll` (old SwitchScene + old ctor) → man_mesh + crash.

Fixed: usings restored + RuntimeGameplay hijack + SceneContext-only ctor + engine patch.
