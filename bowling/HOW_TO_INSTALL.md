# Install

Castle compiles `Scripts/` on Play. Nothing here is copied into the engine.

1. Copy the whole `bowling` folder to `Documents/CastleBuilder/Projects/bowling`.
   The project root is the folder that contains `project.json`.
2. Launch Castle and use Load Project on that folder.
3. Press Play.

`BowlingLaunch` sends Play's `RuntimeGameplay` scene to `BowlingScene`. The alley in the scene editor is the same class, drawing only, with no physics.

Scripts reference `SiegeEngine.dll` and `Foundation.dll`. The IDE fills those in when it builds `Scripts/`. Do not commit a copy of the engine into `Scripts/Libs`.

If Play fails to compile, the Castle tree is older than the scripting API this project targets: `ShaderProgram.FromId`, `ShaderId.Grid`, and `DrawIndexed`.
