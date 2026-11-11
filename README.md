# SE Blueprint Editor

A desktop editor and analyzer for **Space Engineers** ship blueprints. Open a blueprint from your blueprints folder, see it in 3D with the game's own models and textures, repaint or remove blocks, check how well it is armored, and save it back so the game can load it.

> Unofficial fan tool. Not affiliated with or endorsed by Keen Software House. No game files are included: the editor reads models, textures and block data from your own Space Engineers install while it runs.

## Features

- **3D view with real models.** Functional blocks use the game's `.mwm` models, including turret heads, barrels and other moving parts. Armor blocks are generated for all 37 armor shapes.
- **Game textures.** Light/heavy armor patterns, panel lines and wear, with paint applied. Texture quality can be lowered to save memory.
- **Editing.** Select blocks in 3D or from the block list, paint and delete them, with undo/redo.
- **Safe saving.** The first save keeps the original as `bp.sbc.bak`, and every save deletes the `bp.sbcB5` cache so the game loads your changes. *Save as new blueprint* leaves the original untouched.
- **Nothing is lost.** Every field in the blueprint, including ones the editor doesn't understand, is written back exactly as it was read.
- **Workspaces.** Switch between **Edit** and **Analyze** on the same blueprint, keeping your camera and selection.
- **Armor heat map** (Analyze workspace):
  - *Hull thickness*: outer armor colored by how much armor is directly behind it.
  - *Exposed components*: armor turns see-through, and reactors, cockpits, gyros and other key blocks are colored by how much damage it takes to reach them from their weakest side.

## Requirements

- Windows 10 or 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Space Engineers installed through Steam (found automatically; otherwise use **File → Set game folder…**)
- A DirectX 11 graphics card

## Getting started

```
git clone <this repository>
cd "SE blueprint editor"
dotnet run --project src/SEBlueprint.App
```

Then press **Ctrl+O** and pick a `bp.sbc`. The dialog starts in `%AppData%\SpaceEngineers\Blueprints\local`. You can also drag a blueprint folder onto the window.

To build a standalone copy:

```
dotnet publish src/SEBlueprint.App -c Release -o publish
```

## Using the editor

### 3D view

| Action | Control |
|---|---|
| Rotate | Right-drag |
| Pan | Middle-drag |
| Zoom (toward the cursor) | Mouse wheel |
| Frame the whole ship | **F** |
| Select a block | Left-click |
| Add/remove from selection | Ctrl+click |
| Clear selection | Esc |

Selecting a grid or a block type in the list on the left selects all of its blocks.

### Editing (Edit workspace)

| Action | Shortcut |
|---|---|
| Open | Ctrl+O |
| Save | Ctrl+S |
| Save as new blueprint | Ctrl+Shift+S |
| Paint selected blocks | P (choose the color with the H/S/V sliders) |
| Delete selected blocks | Del |
| Undo / Redo | Ctrl+Z / Ctrl+Y |

The editor won't delete every block of a grid, because the game can't load a blueprint with an empty grid.

### Analyzing (Analyze workspace)

Analyze is read-only, so editing commands are disabled there. Choose *Hull thickness* or *Exposed components*, read the red → yellow → green legend, and click a row in the weakest-first list to select that block.

The numbers are hit points taken from the game's block data (light armor 2,500 HP, heavy armor 16,500 HP). Damage types, deformation and weapon penetration aren't simulated, so use the results to compare one part of the ship with another, not to predict exact combat outcomes.

### View options

- **Model detail:** Boxes, Low, Medium (default), High or Full. Lower is faster on very large ships.
- **Show textures:** turn textures on or off.
- **Texture quality:** Low 256 px, Medium 512 px, High 1024 px (default) or Full 2048 px. Each step down uses about a quarter of the memory.

Settings are stored in `%AppData%\SEBlueprintEditor\settings.json`.

## Project layout

```
src/
  SEBlueprint.Core/       Blueprint model, reading/saving, block definitions, .mwm model reader,
                          armor shapes, editing commands with undo, armor analysis. No UI code.
  SEBlueprint.Rendering/  Turns blueprints into 3D scenes (HelixToolkit.Wpf.SharpDX), texture loading.
  SEBlueprint.App/        WPF application: window, workspaces, view models.
tests/
  SEBlueprint.Tests/      xUnit tests for Core.
```

Dependencies only point one way, App → Rendering → Core, so the core logic can be tested without a UI.

### How it reads the game

- **Blueprints** (`bp.sbc`) are XML. Each block wraps its original XML element, which is how fields the editor doesn't know about survive a save.
- **Block definitions** come from `Content\Data\CubeBlocks\*.sbc`, with English names from `Content\Data\Localization\MyTexts.resx`.
- **Models** come from `.mwm` files. The model reader handles their section index, half-float vertices, materials, LOD levels and the "subpart" attachment points used for turrets and doors.
- **Textures** are the game's BC7 `.dds` files. They're decoded with [BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET), because the game keeps metalness in the alpha channel and the 3D library would otherwise draw blocks see-through.

## Running the tests

```
dotnet test
```

## Known limitations

- Paint tints whole model parts, so a few small details the game leaves unpainted get tinted too.
- Decals and labels, and bump mapping from the game's normal maps, aren't drawn yet.
- Rounded armor shapes are drawn with sharp edges, and a few rare armor transition shapes are approximations.
- Mod blocks without definitions show as magenta boxes.
- Deleting a block that something else points to (for example a rotor holding a subgrid) leaves that link dangling.

## License

No license has been chosen yet.
