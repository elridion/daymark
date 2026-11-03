# Daymark code specifics

Adds to the workspace-wide `code-style.md` and `valheim-modding.md` in `../.claude/rules/`.

## Reading the sky

- Time comes from `EnvMan.GetDayFraction()`, the smoothed sky time vanilla uses to place the sun
  (0.25 sunrise, 0.5 midday, 0.75 sunset, 0 midnight). Vanilla rescales the world clock so daylight
  takes 70% of it; phases are cut on the rescaled value, so they follow the visible sun.
- The sun direction is rebuilt from the rotation in `EnvMan.SetEnv` (`m_sunAngle` of the current
  environment and the day fraction). Never read `m_dirLight` instead: vanilla flips that light by
  180 degrees whenever night intensity is above zero, which already happens during dusk.
- Night is read from the stars, not the moon. 0.1.0 placed the moon straight across the sky from
  the sun (the flipped night light); in game (2026-11-02) the skybox's moon was elsewhere, so the
  moon was dropped rather than chased.

## Gaze rules

- By day the sun counts only above about 1.5 degrees elevation and within `GazeAngle` of the
  camera forward. By night (sky time before 0.25 or from 0.75) any look at least `StarElevation`
  (about 35 degrees) up counts. Only then is the view checked for open sky: no hit along it
  (`RaycastNonAlloc`, `OcclusionRange` 1500 m, terrain, static, piece and vehicle layers) other
  than vegetation (a `TreeBase`, or a `Destructible` of type `Tree`: bushes, stumps), and not in
  an `m_alwaysDark` environment (Mistlands). Vegetation was ignored after testing (2026-11-02):
  a crown is one solid collider, so any canopy, or a forest on the horizon at a low sun, hid the
  sky. A full hit buffer (`MaxHits` 32) counts as blocked. Weather is ignored, so overcast nights still count. Menus, map, inventory, text
  input, console and the radial menu suppress it.
- Combat suppresses it: the player counts as in combat while `Player.IsTargeted()` (the signal
  that starts vanilla's combat music) or within `CalmTime` (5 s) of their own last attack
  (`Humanoid.m_lastCombatTimer`, protected, read through a cached `AccessTools.FieldRefAccess`).
  The sky is read only after `CalmTime` seconds out of combat.
- The message shows once after `GazeTime` seconds through `MessageHud.ShowMessage` (Center, not
  logged) and re-arms after the gaze has been away for a second.
- The check runs every 0.1 s from the plugin's `Update`: keep it allocation-free.
- Localization keys: `daymark_sun_*` (seven day phases), `daymark_stars_*` (five night phases),
  `daymark_compass_n/e/s/w` (compass point initials), `daymark_deposit_pin` (the deposit hint).

## Worlds without a map

- Everything here is gated on `Game.m_noMap` (the world's `nomap` key or the player's own map
  setting), read when the sky message fires; with a map the reading is unchanged.
- On a reading `SkyGlance` calls `CompassStrip.Show`; the wisp does not depend on readings.
- `CompassStrip` is a `RectTransform` under `Hud.m_rootObject` (vanilla hides the HUD by moving
  that root off screen, so the strip hides with it), built lazily and rebuilt when the `Compass`
  style changes. It spans `Span` (120°) over `Width` px, a mark every 15° (a text at the four
  cardinals, longer ticks at the intercardinals), and fades marks towards its edges with
  `CanvasRenderer.SetAlpha` and the whole strip with a `CanvasGroup`. Marks are a muted bone
  (`s_markColor`, darkened from cream on 2026-11-02 at the user's request), north in ember red
  (`s_northColor`). Facing is the camera's
  `eulerAngles.y` (the camera never rolls, so it holds while looking straight up); north is +Z,
  as on vanilla's map. It stays `CompassTime` seconds, then fades over `FadeTime`.
- Runes use vanilla `Valheim-Rune` (found like Hearthfire's `RuneFont`), which draws Latin letters
  as Elder Futhark: "n a s w" give ᚾ ᚨ ᛊ ᚹ, the first runes of norðr, austr, suðr, vestr. Without
  the font it warns and uses letters. Letters use `Hud.m_hoverName`'s font.
- The wisp needs no reading and ignores Rested: whenever it is night (`EnvMan.IsNight()`), the
  player is alive and has a bed (`PlayerProfile.GetCustomSpawnPoint`, the goal) and no wisp
  exists, `NightWisp.Update` starts one, retrying every `RetryInterval` 5 s; so it appears at
  nightfall, on entering a world at night and after a death. It ends only at dawn (or death). On
  the same 5 s tick a changed bed (slept in a new one) becomes the goal (`WispGuide.SetGoal`). A
  text line naming the bed's direction and distance ("home sense"), a moon reading that called
  the wisp back, and a per-character memory of seen ships and portals were built and removed at
  the user's request: the wisp is magic and knows every ship and portal.

## The wisp guide

- **Look.** A local copy of the Wisplight's light ball (`Demister` item, its
  `SE_Demister.m_ballPrefab`). That ball is networked and clears Mistlands mist, so the template is
  instantiated with `ZNetView.m_forceDisableInit` (its ZNetView then destroys itself without a
  ZDO), deactivated and stripped to `s_keptTypes` (lights, particles, meshes, trails) in several
  `DestroyImmediate` passes; `Demister`, its `ParticleSystemForceField`, `LightFlicker`, `LightLod`
  and sounds go. Rebuilt when a logout's scene unload destroys it. Lights at `LightScale` (60 %).
  The workbench extension connection sparkle was planned first and dropped: it would start out of
  view while the player looks up, inside the player model, and through the camera when home is
  behind.
- **World map (`NavGrid`).** `CellSize` 16 m over the 10 km world radius (1250² cells), sampled
  with `WorldGenerator.GetHeight` on a background thread (vanilla's `HeightmapBuilder` calls the
  generator from its own thread), started by `WispGuide.KeepGrid` as soon as a player is in a
  nomap world with `Wisp` on, rebuilt when `WorldGenerator.instance` changes. Flags: `Walk`
  (shallower than `WadeDepth` 1.6 m), `Sail` (at least `SailDepth` 2.5 m), `Swim` (deep water with
  land on both sides within `SwimGap` 2 cells along a row, column or diagonal: rivers and straits
  about 30 m wide), `Coast` (sailable next to land). Player terrain edits and buildings are not
  in it.
- **Routes (`RouteFinder`).** A* on two layers, foot and sail, costs in rough seconds: walk
  1/6 s per m times `1 + SlopeFactor * grade` (grade above `MaxGrade` 1.2 is a cliff), swim
  1/1.5, sail 1/12 (coast x1.5, too shallow x3). Foot to sail only within `BoardReach` 2 cells of
  a ship (`BoardCost`), sail to foot onto any walkable neighbour (`LandCost`), portal
  links join entry and exit foot cells (`PortalCost`). A cell can hold many links (portal hubs),
  so `m_portalLinks` maps an entry cell to link indices and `m_arrivedBy` records which link
  reached a node; legs are cut there, not by detecting jumps (connected portals can stand in
  neighbouring cells). The bed's own cell and every portal entry cell are always enterable on
  foot (the bed's from a ship too), whatever their flags or slope: a bed or portal on a pier sits
  in a "deep water" cell and was otherwise unreachable. The heuristic is straight distance at sailing cost, capped by the
  distance from the portal exit nearest the goal: admissible and constant time, but with a portal
  by the bed (the usual base) it barely guides the search, so `MaxExpansions` (1.5 M of the 3.1 M
  nodes) bounds every search; beyond it the wisp goes `Straight`. A one-off reverse search over
  the portal graph per route would give a sharper bound if searches prove slow. Runs on the
  thread pool, one search at a time, reusing its 2 x 1250² buffers. Cell paths are thinned with
  Douglas-Peucker (8 m) into legs: `Walk`, `Sail`, `Portal` (entry, exit and the `PortalLink` with
  both ZDOIDs), `Straight`; a walk leg into a portal ends exactly at it and the next starts at its
  exit.
- **Ships and portals (`WorldDirectory`).** The wisp knows every one. A server holds all ZDOs but
  sends a client only those near it, so a client sends the server an empty `DaymarkDirectory`
  request (Jotunn `CustomRPC`, registered in `Awake`, sent to `ZNet.GetServerPeer().m_uid`;
  `ZRoutedRpc.GetServerPeerID` is private) every `RequestInterval` 20 s. A server with Daymark
  answers with every portal (`ZDOMan.GetPortalList()`: ZDOID, position, rotation, connection) and
  every ship (ZDOID, position). Ships have no index of their own, so every peer that needs them
  (a client in a nomap world with `Wisp` on; a server in a world with the global `nomap` key, or
  asked within `ServerScanWindow` 120 s) runs vanilla's `GetAllZDOsWithPrefabIterative` one step
  per frame (400 sectors) for each prefab with a `Ship` in `ZNetScene.m_prefabs`, restarting
  `ShipScanInterval` 20 s after each pass. A finished pass keeps ZDOIDs, resolved on use with
  `ZDOMan.GetZDO` and a check against the ship prefab hashes: vanilla pools ZDO objects, so a
  kept `ZDO` of a destroyed ship turns into the next object created. A new `ZNet` (another world
  or connection) clears everything learned from the last one, for hosts too. A server without
  Daymark never answers; after `MaxUnanswered` 3 tries the client stops asking until it joins
  again. Hosts and single player use their own lists. Every route merges the server's rows with
  the local game's copies, the local copy winning only on a host, for objects the server did not
  list, or where the area is loaded (`ZNetScene.IsAreaReady`): a client keeps every ZDO it ever
  received but the server updates only nearby ones, so a far copy may predate a retag or a
  voyage. Portal exits are where vanilla places a player (a metre out of the partner's front).
  Connections are never stored: retagging makes vanilla's `Game.ConnectPortals` reconnect within
  5 s, so the next route sees it. A ship found missing where a route placed it is left out for
  `MissingShipTime` 60 s (`MarkShipMissing`) until the lists catch up.
- **Guiding (`WispGuide`).** States: `Waiting` beside the player until the camera looks below
  `LookDownLimit` (about 20° up) or `WaitTime` 10 s; `Planning` until a route arrives; `Leading`
  `WalkLead` 12 m (`SailLead` 35 m at sea) ahead along the current leg, height following the
  ground (sea: water level + 4 m); `AtShip` circling `MastHeight` above the ship's
  `m_mastObject` until the player is in any ship (`Ship.GetLocalShip`); `AtPortal` tracing a
  vertical ring in the portal's plane (`right`/`up`), `PortalRingOffset` in front on the player's
  side, until a teleport ends (`Player.IsTeleporting` turning false, or a jump over
  `TeleportJump` 100 m for other teleports). In both, walking more than `HandOverDistance` +
  `WalkStray` from the ship or portal re-plans. `Home` once the player is within
  `ArriveDistance` 15 m of the bed (or starts within `LeaveDistance` 60 m, spawning over the bed):
  resting low over the bed as if asleep there (the user's design): `Swirl` wanders it slowly
  over the bed's surface, `HomeLength` 0.6 m along and `HomeWidth` 0.3 m across, rising and
  sinking between 0.5 and 1 m (`HomeHeight` 0.75 m, `HomeRise` 0.25 m), each axis a sine at its
  own speed (0.13, 0.21, 0.4 rad/s) so the path never repeats; the quick flight wobble is off
  there and it drifts to the swirl point at `HomeDrift` 3 m/s. A circling ring 2.2 m up was too
  busy in game. Until the bed is found it swirls within `HomeWidth` both ways. The bed's
  length is the flat forward of the `Bed` whose `m_spawnPoint` is within 1 m of the goal (an
  `OverlapSphere` once a second until found; unconfirmed in game that forward runs along the
  bed), until the player goes past `LeaveDistance`, which starts a new route; `Fading` only at
  dawn. In any state but `Fading`, a wisp more than `LostDistance` 150 m from the player (at
  home: from the bed) jumps to them: a portal trip left it behind otherwise, trailing after the
  player at flying speed or drifting home at `HomeDrift` (seen in game 2026-11-02, mostly when
  leaving home through a portal, where `Home` turns into `Planning` with the wisp at the bed). At a portal it finds the planned one
  by ZDOID (a hub has several side by side) and re-plans if it is gone or its connection is no
  longer the planned exit (retagged; an unconnected portal mid-retag is waited for). It re-plans
  when the player strays (`WalkStray` 40 m, `SailStray` 120 m, at most every `ReplanInterval`
  5 s). `Restart` drops the route and waits beside the player for a new one, at once, on
  boarding, leaving a ship away from the landing, a teleport, a missing ship or portal, leaving
  home, a new bed, or coming out of a dungeon; the world map finishing after a provisional
  straight route also re-plans. Searches are numbered: asking during a running search queues a
  fresh one and drops the running one's result, and removing the wisp (death, dawn, logout)
  drops any result still on its way, so no route from another life or world is followed. In a
  dungeon (`Character.InInterior`, 5 km up) the wisp is hidden and paused, and night falling there
  never counts as home. Turning `Wisp` off fades an existing wisp (`Dismiss`) and stops the ship
  scan and requests. A missing light ball is looked for once per `ObjectDB` (world), not once per
  game. Ship and portal lookups run once a second.
- Confirmed in game by the user (2026-11-02): the wisp's look and its rest over the bed.
- Not yet confirmed in game: the rune glyphs on the HUD, the strip clearing the raid event bar,
  the compass colours, trees letting the sky through, how long the world map
  takes to build and a search takes, the route quality (cliffs, rivers, sea routes), the mast and
  portal rings, the directory RPC on a dedicated server, how long the ship scan takes on a big
  world, and routes through hubs and after retagging.

## Dungeon pins

- Toggled by `DungeonPins` (default off, the user's choice), read when a pin would be placed.
  Placed in every world, nomap included: the character keeps the pin and it shows once the map
  is on. `DungeonPins.Update` remembers the local player's position every frame
  while outside; the frame `Character.InInterior` turns true (dungeons sit 5 km up), that last
  outside position is the entrance, at its door. Dying or no player forgets it, so logging in
  inside a dungeon pins nothing.
- The pin is placed with `Minimap.DiscoverLocation(entrance, PinType.Icon2, label, showMap:
  false)`, the call a Vegvisir's boss answer ends in (`Game.RPC_DiscoverLocationResponse`): it
  skips a similar pin, adds a saved one and shows vanilla's `$msg_pin_added` message (with
  `showMap` true it would open the map at the pin instead). It is skipped beforehand when `Minimap.HavePinInRange` finds any pin within `SameEntrance` 10 m (the player's
  own counts); that method is private, so it is called through a cached
  `AccessTools.MethodDelegate` (the first build called it directly and every dungeon entry threw
  `MethodAccessException` in game, 2026-11-03). The label is the localized `Location.m_discoverLabel` of
  `Location.GetLocation(entrance, false)`, or empty. Saved pins live in the character's map
  data, the one thing Daymark saves; they stay after uninstalling, as ordinary pins.
- Confirmed in game by the user (2026-11-03): a pin shows up on entering a dungeon.
- Not yet confirmed in game: that `Icon2` is the hammer (Mjolnir) icon the user asked for, that
  `AddPin` works in a nomap world, that dungeons fill
  `m_discoverLabel`, and that the last outside position is at the door for every dungeon type.

## Deposit pins

- Toggled by `DepositPins` (default off), read live. Which deposits count is a rule agreed with
  the user (2026-11-03): big deposits mined node by node, yielding metal, in the open world, that
  stay put. In: copper deposit (`rock4_copper`), silver vein (`silvervein`), ancient armor
  (`giant_helmet1/2`) and ancient sword (`giant_sword1/2`), each with its fractured prefab
  (`_frac`, `_destruction`), which vanilla spawns at the same position on the first hit. Out:
  tin and obsidian (single small `MineRock`s), the flametal vein (on a Lavaiathan that sinks), the
  iron vein (console only), muddy scrap piles (crypt ones are covered by dungeon pins; the user
  left the buried swamp ones out). Prefab names are from the wiki; a new deposit means a new row in
  `s_deposits` and the README table.
- Label: the vanilla metal name (`$item_copper`, `$item_silver`, `$item_iron`; ancient remains
  drop scrap iron and copper scrap, pinned as Iron), localized when placed. Icon `Icon3`, the dot.
- The deposit is `Player.GetHoverObject()` (any collider within interact reach, Hoverable or not)
  up to its `ZNetView`, by prefab name, cached per hover object. Use (`Use`/`JoyUse`) held
  `HoldTime` 0.6 s on the same deposit places the pin, then waits for release. Deposits have no
  `Interactable`, so vanilla's own hold-interact does nothing there. Placed with
  `Minimap.DiscoverLocation(..., showMap: false)` at the deposit's root position: it shows "Pin
  added", or returns false for a same-name pin within 1 m, then `$msg_pin_exist` is shown.
- The hint is a postfix on the private `Hud.UpdateCrosshair` (by string name), appending
  `[$KEY_Use] $daymark_deposit_pin` to `m_hoverName` with vanilla's gamepad sprite rewrite. Daymark's
  only Harmony patch.
- Not yet confirmed in game: the prefab names, that every listed deposit is hovered within reach
  (big meshes: the root position may be metres from the crosshair point), the hint layout, and
  the dot icon.
