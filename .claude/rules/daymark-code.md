# Daymark code specifics

Adds to the workspace-wide `code-style.md` and `valheim-modding.md` in `../.claude/rules/`.

## Reading the sky

- Time comes from `EnvMan.GetDayFraction()`, the smoothed sky time vanilla uses to place the sun
  (0.25 sunrise, 0.5 midday, 0.75 sunset, 0 midnight). Vanilla rescales the world clock so daylight
  takes 70% of it; phases are cut on the rescaled value, so they follow the visible sun.
- The sun direction is rebuilt from the rotation in `EnvMan.SetEnv` (`m_sunAngle` of the current
  environment and the day fraction). Never read `m_dirLight` instead: vanilla flips that light by
  180 degrees whenever night intensity is above zero, which already happens during dusk.
- The moon is taken as straight across the sky from the sun, matching that flipped night light.
  Not yet confirmed against the moon the skybox draws; if the moon does not respond in game, this
  vector in `SkyGlance.GazedPhase` is the first suspect.

## Gaze rules

- A body counts only above about 1.5 degrees elevation, within `GazeAngle` of the camera forward,
  with no hit on a raycast against terrain, static, piece and vehicle layers, and not in an
  `m_alwaysDark` environment (Mistlands). Menus, map, inventory, text input, console and the radial
  menu suppress it.
- The message shows once after `GazeTime` seconds through `MessageHud.ShowMessage` (Center, not
  logged) and re-arms after the gaze has been away for a second.
- The check runs every 0.1 s from the plugin's `Update`: keep it allocation-free.
- Localization keys: `daymark_sun_*` (seven day phases), `daymark_moon_*` (five night phases).
