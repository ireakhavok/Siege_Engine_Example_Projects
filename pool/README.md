# Pool

A table example for SiegeEngine. Eight-ball, nine-ball, and cutthroat. One to four players. Cutthroat is three players.

The engine steps the balls. Cushions and the slate are static boxes. Balls are spheres. A pocket is a rules test on the ball centre after the step, because the engine has no sensor volume.

`Shape` is not publicly settable on the vendored DLL, and `RebuildShape` builds a box or a mesh. `PoolTable.AssignSphere` uses the non-public setter and then `RecomputeMassProperties`. That is the assign the engine already uses inside `RebuildShape`.

Play opens the scene through `PoolLaunch`, which registers `RuntimeGameplay`. The menu is `UI/menu.html`, opened with `OpenGameHudEvent` at 400 by 308.

Aim with A and D. Hold Space to set power, release to shoot. A scratch is ball in hand: A and D move the cue, Space places it.
