# Daymark

Look at the sun or up at the stars to get a rough sense of the time of day. No clock on the screen, no
numbers: just what a Viking could read from the sky.

## Why

Clock on screen say 14:37. Viking not know what 14:37 is. Viking know sun.

Look at sun. Sun high: midday. Sun low: go home. Look at stars. Stars bright: midnight, troll awake,
go to bed.

## How it works

- By day, look straight at the sun for a moment (about a second) and a short line appears in the
  centre of the screen, like "The sun hangs low. Late afternoon."
- By night, look well up into the sky (about 35 degrees or more) at the stars instead.
- Look away and back to read the sky again.
- The sun must be above the horizon and in plain sight, and the stars must be open sky where you
  look. Mountains, rocks, roofs and walls block them, and the Mistlands' gloom hides the sky
  entirely. Trees do not: the sky shows through their leaves.
- No stargazing in a fight: while a creature is hunting you or you are attacking, the sky stays
  silent. It speaks again 5 seconds after the fight is over.
- The reading follows the sky you see, not the raw world clock, so "midday" is when the sun is
  actually at its highest.

| By day | By night |
| --- | --- |
| The sun has just risen. Early morning. | The first stars come out. Nightfall. |
| The sun climbs. Morning. | The stars turn overhead. Night. |
| The sun is nearly at its height. Late morning. | The stars shine their brightest. Midnight. |
| The sun stands high. Midday. | The stars wheel on. The small hours. |
| The sun has begun to sink. Early afternoon. | The stars are fading. Dawn is near. |
| The sun hangs low. Late afternoon. | |
| The sun is setting. Evening. | |

## Worlds without a map

Viking have no map. Viking have sky.

In worlds played with the "no map" setting, the sky also helps you find your way:

- A compass appears at the top of the screen after the sky speaks, its marks sliding past as you
  turn. By default it shows the first runes of the Old Norse words for north, east, south and
  west (ᚾ ᚨ ᛊ ᚹ), with north in red; set `Compass` to `Letters` for the compass points of your own
  language, or to `Off`.
- The bearing drifts as you travel, as when finding your way by reckoning: every 250 m (a quarter
  of `CompassDistance`) one point turns into a plain stroke, the direction you travelled towards
  least going first, so the one you are heading for is kept longest. Standing still loses nothing.
  With all four gone the compass fades; read the sky again to get them back. A portal, a dungeon
  or death loses them all at once.
- When night falls, a small wisp appears, as long as you have a bed. It stays with you all night
  and fades at dawn. At home it rests low over your bed, drifting slowly; away from home it flies ahead of you
  on the quickest way back it knows:
  - Over land it follows the lay of the land around cliffs and through valleys. Shallow water
    and narrow rivers are waded or swum, wider water needs a ship.
  - If a ship lies on your side of the water, the wisp circles its mast until you climb aboard,
    then leads you along open water deep enough to sail, and on from the shore.
  - If a portal leads closer to home, the wisp circles in front of it, in the portal's doorway,
    until you step through, and comes through with you. It takes you through hubs too, and if someone retags a portal it
    finds another way.
  - If there is no way across the water, it simply flies straight towards your bed.
  - Stray from its path, or walk away from the ship or portal it waits at, and it finds a new
    one. Once you are home it settles over your bed again; sleep in a new bed and that becomes
    home. In a dungeon it waits outside.

The wisp is magic: it knows every ship and every portal in the world, and where each portal leads
right now. Its sense of the land is the world as it was made, not your own digging, walls or
bridges.

With the map on, none of this appears.

## Dungeon pins

Off by default: set `DungeonPins` to `true`. Then going into a dungeon (burial chambers, caves,
crypts and the like) puts a hammer pin on your map at its entrance, named after the place when
the game gives it a name, and the game tells you "Pin added" just as when a Vegvisir marks a
boss. If a pin already marks that entrance, yours or an earlier one, no
second pin is added. They are ordinary pins: rename or remove them as you like. In worlds
without a map the pins are kept too and show once the map is turned on.

## Deposit pins

Off by default: set `DepositPins` to `true`. Then the big ore deposits you come back to show
"Hold E: pin on map" under their name; hold the use key for a moment and a dot pin named after the
metal goes on your map:

| Deposit | Pin |
| --- | --- |
| Copper deposit (Black Forest) | Copper |
| Silver vein (Mountains) | Silver |
| Ancient armor and ancient swords (Mistlands) | Iron |

Small rocks you empty in one go (tin, obsidian), the flametal veins riding the Lavaiathans and
muddy scrap piles get no pin. Once a deposit has its pin the hint goes away, and holding the key
there again only says "Pin already exists". These too
are ordinary pins, kept in worlds without a map.

## Configuration

`BepInEx/config/elridion.daymark.cfg`. Each player sets their own values.

| Setting | Default | Meaning |
| --- | --- | --- |
| `GazeAngle` | 6 | How close to the sun you must look, in degrees from the centre of the screen; at night any look well up at the stars counts (2-20) |
| `GazeTime` | 0.75 | Seconds you must keep looking before the time of day is shown (0-5) |
| `Compass` | Runes | Compass shown in worlds without a map: `Runes`, `Letters` or `Off` |
| `CompassDistance` | 1000 | Metres you can travel after reading the sky before the compass has lost all four points, one every quarter (100-5000) |
| `Wisp` | true | In worlds without a map, a wisp stays with you at night: it floats over your bed at home and leads you there from afar |
| `DungeonPins` | false | Pin a dungeon's entrance on the map when you go in, unless a pin already marks it |
| `DepositPins` | false | Hold the use key on a copper deposit, silver vein or ancient armor or sword to pin it on the map, named after its metal |

## Languages

The messages follow the game's language setting, in every language Valheim offers except Abenaki,
which shows English. The translations have not been checked by native speakers yet; corrections
are welcome on GitHub.

## Requirements

- BepInEx for Valheim
- [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)

Client-side: install it for yourself; other players and the server do not need it. On a dedicated
server, the wisp learns about ships and portals far away only if the server has Daymark installed
too; without it, it knows only those your game has already loaded nearby.

## Uninstalling

Remove the mod from your mod manager. It saves nothing in your world. Dungeon and deposit pins
it placed stay on your character's map as ordinary pins.

## Credits

- Mod icon: "Eclipse" by Lorc from [game-icons.net](https://game-icons.net), licensed under
  [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). Recoloured and outlined.
