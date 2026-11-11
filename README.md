# SE Blueprint Editor
testing
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

### Placing blocks (Edit workspace)

Pick a tool in the **Tool** section of the left panel. Only one is active at a time:

| Tool | Key | Left click |
|---|---|---|
| Select | 1 | Selects blocks (as before) |
| Place on face | 2 | Puts the new block against the face of the block under the mouse, like building in the game |
| Place on plane | 3 | Puts the new block on the blue work-plane grid, even in empty space |

While placing, a see-through preview follows the mouse: a green box means it fits, red means another block is in the way or the block is the wrong size for that grid.

- **Choosing a block:** search the list and pick Large or Small. *Copy selected block*, or Ctrl+click a block in the 3D view, copies its type, rotation and color.
- **Rotating:** R, T and Y turn the block around the up, left-right and front-back axes. Shift turns the other way.
- **Color:** new blocks get the color set in the Paint section.
- **Work plane:** choose Floor (Y), Side wall (X) or Cross wall (Z), and move it one layer with PageUp/PageDown or the −/+ buttons. *At selection* moves it to the selected block's grid and layer.
- **Stopping:** Esc goes back to Select. Every placement can be undone with Ctrl+Z.

New blocks are written with only their type, position, rotation and color. The game fills in default settings when it loads the blueprint. The editor doesn't check mount points, so it will let you place blocks the game wouldn't allow you to attach there.

**Align grid to selected block** (Edit menu): select one block, usually the cockpit, and the grid is turned so that block faces Forward with its top Up. The ship stays in the same place in the world and subgrids stay attached; only the grid's own forward/up changes, which is what projectors and printers line the ship up with. Conveyor lines and oxygen rooms are rotated with the grid. Armor deformation and build-mode mirror planes are cleared.

### Analyzing (Analyze workspace)

Analyze is read-only, so editing commands are disabled there. Choose *Hull thickness* or *Exposed components*, read the red → yellow → green legend, and click a row in the weakest-first list to select that block.

The numbers are hit points taken from the game's block data (light armor 2,500 HP, heavy armor 16,500 HP). Damage types, deformation and weapon penetration aren't simulated, so use the results to compare one part of the ship with another, not to predict exact combat outcomes.

### View options

- **Model detail:** Boxes, Low, Medium (default), High or Full. Lower is faster on very large ships.
- **Show block outlines (O):** draw the edges of every armor block, so a single block stands out in a wall of armor. On by default. **Outline width** sets the lines to Thin (1 px, default), Medium (2 px) or Thick (3 px). Outlines fade out as you zoom away, so they never cover the blocks on a zoomed-out view (thicker lines fade sooner). Functional blocks with real models aren't outlined, and neither is see-through armor in the analysis views.
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
- Deleting one end of a rotor, hinge, piston, suspension or connector link leaves the other end pointing at a missing block. The editor warns and asks before such a delete, but doesn't repair the link.

## License

No license has been chosen yet.
