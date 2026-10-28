# Daymark

Valheim mod (BepInEx + Jotunn) that shows a vague time of day when the player looks at the sun or
the moon. Client-side only.

Players: install it from a mod manager. Features and settings are on the mod page, whose source is
[`package/README.md`](package/README.md).

## Building

Requires the .NET SDK (8+) and a Valheim install. The build locates Valheim through the Steam
registry entry; override with `-p:VALHEIM_INSTALL="<path>"`.

```
dotnet build -c Release                           # build bin/Release/Daymark.dll
dotnet build -c Release -t:Package                # also write dist/Daymark-<version>.zip
```

The first build generates publicized Valheim assemblies in `Valheim_Data/Managed/publicized_assemblies`.

The build touches nothing outside the repo. To test in game, install the zip with a mod manager
(e.g. import it into a [Gale](https://github.com/Kesomannen/gale) profile).

## Project layout

| Path | Purpose |
| --- | --- |
| `src/` | Plugin source: config and translations, the gaze check |
| `assets/translations/` | Player-facing text, one JSON file per Valheim language |
| `tools/make_icon.ps1` | Generates `package/icon.png` from game-icons.net |
| `package/` | Mod page README and icon shipped in the package |

## Releases

CI (`.github/workflows/build.yml`) builds the package on every push and pull request. Pushing a
`vX.Y.Z` tag checks that the tag matches the version in `Daymark.csproj` and
`src/DaymarkPlugin.cs` and has a `CHANGELOG.md` section, then creates a GitHub release and
publishes to Hexium.

## License

Code: [MIT](LICENSE). The icon is "Eclipse" by Lorc from [game-icons.net](https://game-icons.net),
licensed under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).
