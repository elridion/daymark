# Daymark

Look at the sun or the moon to get a rough sense of the time of day. No clock on the screen, no
numbers: just what a Viking could read from the sky.

## Why

Clock on screen say 14:37. Viking not know what 14:37 is. Viking know sun.

Look at sun. Sun high: midday. Sun low: go home. Look at moon. Moon high: midnight, troll awake,
go to bed.

## How it works

- Look straight at the sun or the moon for a moment (about a second) and a short line appears in
  the centre of the screen, like "The sun hangs low. Late afternoon."
- Look away and back to read the sky again.
- The sun or moon must be above the horizon and in plain sight. Mountains, trees, roofs and walls
  block it, and the Mistlands' gloom hides the sky entirely.
- No stargazing in a fight: while a creature is hunting you or you are attacking, the sky stays
  silent. It speaks again 5 seconds after the fight is over.
- The reading follows the sky you see, not the raw world clock, so "midday" is when the sun is
  actually at its highest.

| By day | By night |
| --- | --- |
| The sun has just risen. Early morning. | The moon is rising. Nightfall. |
| The sun climbs. Morning. | The moon climbs. Night. |
| The sun is nearly at its height. Late morning. | The moon stands high. Midnight. |
| The sun stands high. Midday. | The moon sinks. The small hours. |
| The sun has begun to sink. Early afternoon. | The moon is setting. Dawn is near. |
| The sun hangs low. Late afternoon. | |
| The sun is setting. Evening. | |

## Configuration

`BepInEx/config/elridion.daymark.cfg`. Each player sets their own values.

| Setting | Default | Meaning |
| --- | --- | --- |
| `GazeAngle` | 6 | How close to the sun or moon you must look, in degrees from the centre of the screen (2-20) |
| `GazeTime` | 0.75 | Seconds you must keep looking before the time of day is shown (0-5) |

## Languages

The messages follow the game's language setting, in every language Valheim offers except Abenaki,
which shows English. The translations have not been checked by native speakers yet; corrections
are welcome on GitHub.

## Requirements

- BepInEx for Valheim
- [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)

Client-side only. Install it for yourself; other players and the server do not need it.

## Uninstalling

Remove the mod from your mod manager. It saves nothing in your world or character.

## Credits

- Mod icon: "Eclipse" by Lorc from [game-icons.net](https://game-icons.net), licensed under
  [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). Recoloured and outlined.
