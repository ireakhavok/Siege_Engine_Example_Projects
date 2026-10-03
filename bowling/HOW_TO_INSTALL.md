# Install

Castle compiles `Scripts/` on Play. Nothing here is copied into the engine.

1. Copy the whole `bowling` folder over the project you load.
   If that is `Siege_Engine_Example_Projects/bowling`, replace that folder.
   The project root is the folder that contains `project.json`.
2. Launch Castle and use Load Project on that folder. Reload if it is already open so scripts rebuild.
3. Press Play. An HTML panel opens over the alley: pick 1–4 players and press Step onto the lane (or Enter). That closes the panel and the lane scene starts. The scoreboard is the HTML panel during a game. It does not list controls.
4. The menu markup is `UI/menu.html`, next to `project.json`. Drop your art in as `UI/MenuBackground.png` (same folder) and press Play again. `{{P1}}` through `{{P4}}` become `picked` or `choice`. Hover is ordinary CSS `:hover`. The start button is the `menu.start` hook.

Pins and the ball are built in code. There is no pin mesh to import.

`BowlingLaunch` sends Play's `RuntimeGameplay` scene to `BowlingScene`. The alley in the scene editor is the same class, drawing only, with no physics.

Scripts reference `SiegeEngine.dll` and `Foundation.dll`. The IDE fills those in when it builds `Scripts/`. Do not commit a copy of the engine into `Scripts/Libs`.

If Play fails to compile, the Castle tree is older than the scripting API this project targets: `ShaderProgram.FromId`, `ShaderId.Grid`, and `DrawIndexed`.
