# Bowling

Ten-pin bowling for Castle. Real lane length, a 60-foot pin deck, USBC pin spacing, a 14 lb ball, and a full ten-frame card including the tenth-frame fill balls.

Pins and the ball are `Assets/pin.fbx` and `Assets/ball.fbx`. The rack is that pin mesh on a dynamic body. You do not model another pin.

## Play

1. Copy this folder to `Documents/CastleBuilder/Projects/bowling`.
2. In the IDE, Load Project and open that folder.
3. Press Play. An HTML panel opens over the alley. Pick 1–4 players, then Step onto the lane.
4. That panel is `UI/menu.html`. Hover styles live in its CSS. The buttons are Siege `data-hook` controls (`menu.start` starts the lane). Put your background at `UI/MenuBackground.png`, next to `menu.html`.

The setup screen and the scoreboard are HTML panels. The setup panel is only there before the first ball. The scoreboard replaces it and shows the card and the power bar. Neither panel lists controls.

Players share the center lane. Each player finishes the frame they are in, then the next player bowls that same frame. A fresh rack is set between players.

| Input | Action |
|---|---|
| Menu: 1–4, the panel, or Enter | Players, then bowl |
| A / D or Left / Right | Move your feet on the approach |
| Mouse left / right | Aim. Gold dots are the shot |
| Q / E | Straighten the path, or bend it left. Red dots are a gutter |
| Hold Space or left mouse, then release | Set power and throw |
| R | Rematch with the same players |
| Enter on the final card | Back to the menu |

The center lane is the one you bowl. Four more lanes sit beside it. Pins are dynamic bodies the whole time. A ball that leaves the lane drops into the gutter and cannot hit the rack.

The ball starts on the approach, in front of the feet marks, on the board you chose with A and D.

Pins that stay up are left for the second ball. A strike or a spare racks a fresh set. The tenth frame gives the extra balls. Knocked-down pins are anything no longer standing on the deck: tipped, in the gutter, or in the pit.
