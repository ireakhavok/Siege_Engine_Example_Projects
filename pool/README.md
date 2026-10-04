# Pool

A 7-foot table, played from behind the cue. The table, the balls and the cue stick are FBX models under `Assets/`. The engine model renderer draws them, and the balls and rails are shadow casters. Physics is the same solver the bowling lane uses: a static slate, static cushion boxes, and a sphere for every ball.

Pass-and-play, one to four players. Cutthroat is three. This is not a network lobby. Project mode stays Single Player so Play does not start a dedicated server.

## Play

Open the folder as a project and press Play.

- 1–4, E, N, C and Enter on the menu. The buttons in `UI/menu.html` do the same thing.
- The camera sits just above and behind the cue ball. Move the mouse, or A and D, to aim.
- Hold Space or the left mouse button to charge, release to shoot.
- After a scratch, WASD places the cue ball in the kitchen. Space sets it down.
- R racks again. Enter on the result returns to the menu.

`UI/hud.html` is the scoreboard. The scene only fills the tokens already in that file.
