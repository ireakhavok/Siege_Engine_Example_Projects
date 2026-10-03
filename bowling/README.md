# Bowling

Ten-pin bowling for Castle. Real lane length, a 60-foot pin deck, USBC pin spacing, a 14 lb ball, and a full ten-frame card including the tenth-frame fill balls.

This is a project, not an engine change. Load it the same way as chess. Pins and the ball are generated when the scene draws. You do not create a pin asset.

## Play

1. Copy this folder to `Documents/CastleBuilder/Projects/bowling`.
2. In the IDE, Load Project and open that folder.
3. Press Play. The scene editor can show the alley before Play; the game runs on Play (and in Play Host).

## Controls

| Input | Action |
|---|---|
| A / D or Left / Right | Move your feet on the approach |
| Mouse left / right | Aim. The gold dots on the lane are the shot |
| Q / E | Straighten the path, or bend it left. Red dots are a gutter |
| Hold Space or left mouse, then release | Set power and throw |
| R | New game |

The center lane is the one you bowl. Four more lanes sit beside it. Pins on your lane and the two next to it are real dynamic bodies. A ball that leaves the lane drops into the gutter and cannot hit the rack.

Gold dots on the lane are the ball path. Q straightens it, E bends it left. If the dots turn red, that shot is a gutter. The same curve is drawn in the small lane at the bottom left.

The ball starts on the approach, in front of the feet marks, on the board you chose with A and D.

Pins that stay up are left for the second ball. A strike or a spare racks a fresh set. The tenth frame gives the extra balls. Knocked-down pins are anything no longer standing on the deck: tipped, in the gutter, or in the pit.
