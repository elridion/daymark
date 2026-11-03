# Changelog

## Unreleased

- New `DungeonPins` setting, off by default: going into a dungeon pins its entrance on your map
  with the hammer icon, named after the place when the game names it, unless a pin already marks
  it. The game says "Pin added", as when a Vegvisir marks a boss. In worlds without a map the pins show once the map is turned on.
- New `DepositPins` setting, off by default: hold the use key on a copper deposit, silver vein or
  Mistlands ancient armor or sword to pin it on your map as Copper, Silver or Iron. The deposit
  shows the hint under its name until it has its pin.

## 0.2.0

- At night, look up at the stars instead of the moon: any steady look well up into open sky tells
  the time and, without a map, brings up the compass. The moon was looked for in the wrong place.
  The night texts now speak of the stars, in every language.
- Trees no longer hide the sun or the stars: the sky shows through their leaves. Mountains, rocks,
  roofs and walls still block it.
- Worlds without a map: reading the sky brings up a compass at the top of the screen for a minute.
  It shows Old Norse runes (ᚾ ᚨ ᛊ ᚹ) by default, north in red; the new `Compass` setting
  switches to the compass points of your language or turns it off, and `CompassTime` sets how
  long it stays.
- Worlds without a map: a wisp stays with you from nightfall to dawn. At home it rests low over
  your bed, drifting slowly; away from home it leads you there along the quickest way: around
  cliffs and across narrow rivers on foot, to any ship (circling its mast until you board) and
  along a sailable route, or through any portal (it comes through with you), hubs included,
  following where each portal leads right now (on a dedicated server, ships and portals far away
  need Daymark on the server). With no way across the water it flies straight. The new `Wisp`
  setting turns it off.
- New texts translated into every language Valheim offers except Abenaki.

## 0.1.0

- First release: look at the sun or the moon for a moment and a vague time of day appears in the
  centre of the screen, from "The sun has just risen. Early morning." to "The moon is setting. Dawn
  is near." Seven phases by day, five by night.
- Only works when the sun or moon is above the horizon and in plain sight: mountains, trees, roofs
  and the Mistlands' gloom hide it.
- Stays quiet in combat: the sky can be read again 5 seconds after the last creature stops hunting
  you and you stop attacking.
- Gaze angle and gaze time are configurable.
- Translated into every language Valheim offers except Abenaki.
- Client-side only: players and servers without the mod can still play together.
