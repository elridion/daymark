# Daymark

Look at the sun or up at the stars to get a rough sense of the time of day. In worlds without a map, a compass and a night wisp help you find your way home.

## Features

- No clock and no numbers: a short line such as "The sun hangs low. Late afternoon." tells you
  what a Viking could read from the sky.
- In worlds without a map, reading the sky also shows a compass at the top of the screen. Its
  points fade one by one as you travel, so read the sky again now and then.
- In worlds without a map, a wisp stays with you at night. It rests over your bed at home and
  leads you back from afar, over land, by ship and through portals.
- Optional dungeon pins: going into a dungeon pins its entrance on your map.
- Optional deposit pins: holding the use key on a copper deposit, silver vein or ancient armor
  or sword pins it on your map, named after its metal.

## How to use

- By day, look straight at the sun for a moment. By night, look well up at the stars.
- The sky must be in plain sight: mountains, rocks and roofs block it, and the Mistlands' gloom
  hides it entirely. Trees do not.
- The sky stays silent during a fight and speaks again a few seconds after it is over.
- The wisp needs a bed. If a ship or portal is the way home, it circles it until you board or
  step through.

## Installation

- Requires [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).
- Client-side: other players and the server do not need it. On a dedicated server, the wisp
  knows about far-away ships and portals only if the server has Daymark installed too.
- With the map on, the compass and the wisp do not appear.
- The messages follow the game's language. The translations have not been checked by native
  speakers yet.
- Uninstalling is safe: nothing is saved in your world. Dungeon and deposit pins stay on your
  character's map as ordinary pins.

## Configuration

Settings are in `BepInEx/config/elridion.daymark.cfg` (or a config manager). Each player sets
their own.

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| Glance | `GazeAngle` | 6 | How close to the sun you must look, in degrees from the screen's centre (2-20). |
| Glance | `GazeTime` | 0.75 | Seconds you must keep looking before the time is shown (0-5). |
| Wayfinding | `Compass` | Runes | Compass in worlds without a map: `Runes`, `Letters` or `Off`. |
| Wayfinding | `CompassDistance` | 1000 | Metres of travel before each compass point fades (100-5000). |
| Wayfinding | `Wisp` | true | The night wisp in worlds without a map. |
| Wayfinding | `DungeonPins` | false | Pin a dungeon's entrance when you go in. |
| Wayfinding | `DepositPins` | false | Hold the use key on a big ore deposit to pin it. |

## Credits

- The icon is "Eclipse" by Lorc from [game-icons.net](https://game-icons.net), licensed under
  [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). Recoloured and outlined.
