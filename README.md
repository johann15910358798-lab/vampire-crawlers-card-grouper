# VC Card Grouper

`VC Card Grouper` is a BepInEx 6 IL2CPP mod prototype for `Vampire Crawlers`.

It currently targets two workflows:

- hand grouping for large hands
- card face export and replacement for higher-resolution card art

## Current behavior

### Hand grouping

When the hand reaches the configured threshold, the mod groups cards by numeric cost and keeps a stable group bar:

```text
0/3  1/7  2/5  3/0  4/0  5/2  ...  W/4
```

Rules:

- default threshold is 20 cards
- threshold is configurable
- numeric cost groups are stable and scanned from the game card library
- empty groups still appear, for example `4/0`
- `W` cards are always visible
- the active group follows the game's current combo / arithmetic-progression target when it can be read
- if the active target group has no cards, the UI shows `无法连击`

### Card face replacement

The mod creates these directories under `BepInEx/plugins`:

```text
VcCardGrouper/exported-card-faces/
VcCardGrouper/card-faces/
```

When visible card faces are found, original card art is exported to:

```text
VcCardGrouper/exported-card-faces/
```

To replace a card face:

1. take the exported PNG
2. upscale or redraw it
3. put the new PNG into:

```text
VcCardGrouper/card-faces/
```

The replacement PNG can use the exported file name, usually the game `CardConfig.name`, for example:

```text
Card_A_2_ExampleName.png
```

## Build prerequisites

The project follows the `wtksana/vcmod` layout and expects a local `game/` directory or junction pointing to the game install:

```text
vampire-crawlers-card-grouper/game/
```

That directory must contain:

```text
game/BepInEx/core/
game/BepInEx/interop/
```

On Windows, from the repo root:

```powershell
dotnet build .\src\VcCardGrouper\VcCardGrouper.csproj -c Debug --no-restore
```

The build target copies the plugin to:

```text
game/BepInEx/plugins/VcCardGrouper.dll
```

If you use a mod manager such as `r2modman`, copy the built DLL into that profile's:

```text
BepInEx/plugins/
```

This repo does not yet include a Thunderstore package manifest.

## Config

BepInEx will generate:

```text
game/BepInEx/config/johann.vampirecrawlers.cardgrouper.cfg
```

Important settings:

```ini
[HandGrouping]
Enable = true
EnableWhenHandCountAtLeast = 20
FallbackMaxCost = 10
HideInactiveCards = true
UseSetActiveForHiddenCards = false
ShowComboBlockedText = true

[CardFaces]
EnableReplacement = true
ExportOriginalFaces = true
ReplacementDirectory = VcCardGrouper/card-faces
ExportDirectory = VcCardGrouper/exported-card-faces
```

## Notes

- This repo is created on macOS, and this machine currently has no `dotnet` executable and no local `Vampire Crawlers` BepInEx DLLs.
- The pure grouping logic is split into `VcCardGrouper.Core` so it can be tested without Unity.
- The Unity integration still needs a Windows game-side compile and playtest pass.
- The combo target reader first tries to read likely internal combo target fields from `PlayerModel`; if that fails, it falls back to the currently playable numeric group.
