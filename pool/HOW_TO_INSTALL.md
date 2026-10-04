# Pool — install

Open `pool` as a project in Castle and press Play.

The scene class is `PoolScene`. `PoolLaunch` registers it as `RuntimeGameplay`. The models are:

- `Assets/table/table.fbx` — room, cabinet, felt, rails, pockets
- `Assets/cue/cue.fbx` — the stick
- `Assets/balls/ball_00.fbx` through `ball_15.fbx` — cue ball, solids, stripes, the 8

Each FBX sits next to its PNG. The engine loads those as albedo maps. Shadows use the project sun (`sunCastShadows` in `project.json`).

`UI/menu.html` and `UI/hud.html` are the panels. They are not built in code.

If Play says the scripts failed to compile, the Castle tree is older than `ModelRenderer` / `ModelManager.LoadModel`. Use the same engine the bowling project was built against.
