using System.Collections.ObjectModel;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Editing;
using SEBlueprint.Core.Geometry;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.App.ViewModels;

/// <summary>What a left click in the 3D view does in the Edit workspace. Only one tool is active at a time.</summary>
public enum EditTool
{
    /// <summary>Click selects blocks.</summary>
    Select,

    /// <summary>Click a block's face to put the new block against it (like building in the game).</summary>
    PlaceOnFace,

    /// <summary>Click on a flat work plane (one layer of the grid) to put the new block there.</summary>
    PlaceOnPlane,
}

/// <summary>A block type in the palette.</summary>
public sealed record PaletteItem(BlockDefinition Definition)
{
    public string Name => Definition.DisplayName;

    public string Detail => Definition.Size == Vector3I.One
        ? Definition.SubtypeId
        : $"{Definition.SubtypeId}  ({Definition.Size.X}×{Definition.Size.Y}×{Definition.Size.Z})";
}

/// <summary>
/// Placing new blocks: the active tool, the block palette, the new block's rotation, the work plane, and the
/// see-through preview that follows the mouse. The window feeds it the mouse ray; it asks the scene to draw.
/// </summary>
public sealed partial class BuildViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Dictionary<CubeGrid, GridOccupancy> _occupancy = new();
    private DefinitionRepository? _paletteSource;
    private List<PaletteItem> _allItems = new();
    private (Vector3 Origin, Vector3 Direction)? _ray;
    private (Block Block, BlockDefinition Definition, bool Valid, string Reason)? _preview;

    public BuildViewModel(MainViewModel main)
    {
        _main = main;
        // The preview shows the paint color the new block will get.
        main.Paint.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PaintViewModel.ColorMask) && IsPlacing)
                Refresh();
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlacing), nameof(IsSelectTool), nameof(IsFaceTool), nameof(IsPlaneTool), nameof(Hint))]
    private EditTool _tool = EditTool.Select;

    /// <summary>Which axis the work plane is perpendicular to (Y = a floor).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaneX), nameof(IsPlaneY), nameof(IsPlaneZ))]
    private PlaneAxis _planeAxis = PlaneAxis.Y;

    /// <summary>The layer (cell coordinate along <see cref="PlaneAxis"/>) that new blocks go into.</summary>
    [ObservableProperty]
    private int _planeLayer;

    /// <summary>The grid the work plane belongs to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaneGridName))]
    private CubeGrid? _planeGrid;

    /// <summary>Rotation of the block being placed, in grid space.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrientationText))]
    private BlockOrientation _orientation = BlockOrientation.Default;

    [ObservableProperty]
    private string _search = string.Empty;

    /// <summary>Which block size the palette lists. Defaults to the main grid's size when a blueprint opens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLargePalette), nameof(IsSmallPalette))]
    private GridSize _paletteSize = GridSize.Large;

    [ObservableProperty]
    private PaletteItem? _selectedItem;

    /// <summary>What's under the mouse, or why the block can't go there.</summary>
    [ObservableProperty]
    private string _hoverText = string.Empty;

    public ObservableCollection<PaletteItem> Palette { get; } = new();

    public bool IsPlacing => Tool != EditTool.Select;

    // Radio-button friendly views of the enums (WPF radio buttons bind to booleans).
    public bool IsSelectTool { get => Tool == EditTool.Select; set { if (value) Tool = EditTool.Select; } }
    public bool IsFaceTool { get => Tool == EditTool.PlaceOnFace; set { if (value) Tool = EditTool.PlaceOnFace; } }
    public bool IsPlaneTool { get => Tool == EditTool.PlaceOnPlane; set { if (value) Tool = EditTool.PlaceOnPlane; } }
    public bool IsPlaneX { get => PlaneAxis == PlaneAxis.X; set { if (value) PlaneAxis = PlaneAxis.X; } }
    public bool IsPlaneY { get => PlaneAxis == PlaneAxis.Y; set { if (value) PlaneAxis = PlaneAxis.Y; } }
    public bool IsPlaneZ { get => PlaneAxis == PlaneAxis.Z; set { if (value) PlaneAxis = PlaneAxis.Z; } }
    public bool IsLargePalette { get => PaletteSize == GridSize.Large; set { if (value) PaletteSize = GridSize.Large; } }
    public bool IsSmallPalette { get => PaletteSize == GridSize.Small; set { if (value) PaletteSize = GridSize.Small; } }

    public string PlaneGridName => PlaneGrid?.DisplayName ?? "(none)";

    public string OrientationText => $"Front faces {Orientation.Forward}, top faces {Orientation.Up}";

    public string Hint => Tool switch
    {
        EditTool.PlaceOnFace => "Click a block's face to put the new block against it. Ctrl+click a block to copy its type, rotation and color.",
        EditTool.PlaceOnPlane => "Click a square of the blue grid to put the block there. PageUp / PageDown move the plane one layer.",
        _ => string.Empty,
    };

    // ───────────────────────────── Tool and settings changes ─────────────────────────────

    partial void OnToolChanged(EditTool value)
    {
        if (value != EditTool.Select)
        {
            RefreshPalette();
            if (value == EditTool.PlaceOnPlane)
                ResetPlane();
        }
        Refresh();
    }

    partial void OnPlaneAxisChanged(PlaneAxis value)
    {
        ResetPlaneLayer();
        Refresh();
    }

    partial void OnPlaneLayerChanged(int value) => Refresh();

    partial void OnOrientationChanged(BlockOrientation value) => Refresh();

    partial void OnSelectedItemChanged(PaletteItem? value) => Refresh();

    partial void OnSearchChanged(string value) => FilterPalette();

    partial void OnPaletteSizeChanged(GridSize value) => FilterPalette();

    /// <summary>Turns the new block a quarter turn around a grid axis (R = up axis, T = left-right, Y = front-back).</summary>
    public void Rotate(Vector3I axis) => Orientation = Orientation.Rotated(axis);

    [RelayCommand]
    private void LayerUp() => PlaneLayer++;

    [RelayCommand]
    private void LayerDown() => PlaneLayer--;

    /// <summary>Moves the work plane to the selected block's grid and layer.</summary>
    [RelayCommand]
    private void PlaneAtSelection()
    {
        if (_main.SelectedBlocks.Count == 0)
            return;
        PlaneGrid = _main.SelectedBlocks[0].Grid;
        PlaneLayer = Along(_main.SelectedBlocks[0].Min, PlaneAxis);
        Refresh();
    }

    /// <summary>Takes the type, rotation and color of the first selected block.</summary>
    [RelayCommand]
    private void PickFromSelection()
    {
        if (_main.SelectedBlocks.Count > 0)
            PickFrom(_main.SelectedBlocks[0]);
    }

    // ───────────────────────────── Called by the main view model ─────────────────────────────

    /// <summary>A different blueprint was opened.</summary>
    public void OnBlueprintOpened(Blueprint blueprint)
    {
        _occupancy.Clear();
        _ray = null;
        var main = MainGrid(blueprint);
        PlaneGrid = main;
        if (main is not null)
            PaletteSize = main.GridSize;
        if (Tool == EditTool.PlaceOnPlane)
            ResetPlane();
        Refresh();
    }

    /// <summary>Blocks were added, removed or moved: cached cell maps are out of date.</summary>
    public void OnGridsChanged(IEnumerable<CubeGrid> grids)
    {
        foreach (var grid in grids)
            _occupancy.Remove(grid);
        Refresh();
    }

    // ───────────────────────────── Mouse ─────────────────────────────

    /// <summary>The mouse moved: <paramref name="origin"/>/<paramref name="direction"/> is its ray in scene space.</summary>
    public void UpdateHover(Vector3 origin, Vector3 direction)
    {
        _ray = (origin, direction);
        Refresh();
    }

    /// <summary>The mouse left the 3D view.</summary>
    public void ClearHover()
    {
        _ray = null;
        Refresh();
    }

    /// <summary>Left click in the 3D view while placing. <paramref name="pick"/> (Ctrl) copies the block under the mouse instead.</summary>
    public void Click(bool pick)
    {
        if (pick)
        {
            if (_ray is { } ray && RaycastAllGrids(ray) is { } hit)
                PickFrom(hit.Hit.Block);
            return;
        }

        if (_preview is not { } p)
        {
            HoverText = SelectedItem is null ? "Choose a block in the list first." : HoverText;
            return;
        }
        if (!p.Valid)
        {
            _main.StatusText = $"Can't place here: {p.Reason}.";
            return;
        }

        var block = BlockFactory.Create(p.Block.Grid, p.Definition, p.Block.Min, p.Block.Orientation, p.Block.ColorMaskHsv);
        _main.RunEdit(new AddBlockCommand(block, p.Definition.DisplayName));
    }

    // ───────────────────────────── Preview ─────────────────────────────

    /// <summary>Recomputes where the new block would go and redraws the preview and work plane.</summary>
    public void Refresh()
    {
        _preview = null;
        var scene = _main.Scene;
        var blueprint = _main.Blueprint;
        if (scene is null || blueprint is null || !IsPlacing)
        {
            scene?.ShowGhost(null, null, false);
            scene?.ShowWorkPlane(null, PlaneAxis, PlaneLayer, default, default);
            HoverText = string.Empty;
            return;
        }

        if (Tool == EditTool.PlaceOnPlane && PlaneGrid is { } planeGrid)
        {
            var occ = Occupancy(planeGrid);
            scene.ShowWorkPlane(planeGrid, PlaneAxis, PlaneLayer, occ.Min, occ.Max);
        }
        else
        {
            scene.ShowWorkPlane(null, PlaneAxis, PlaneLayer, default, default);
        }

        _preview = ComputePreview(blueprint);
        if (_preview is { } p)
        {
            scene.ShowGhost(p.Block, p.Definition, p.Valid);
            var min = p.Block.Min;
            HoverText = p.Valid
                ? $"Cell ({min.X}, {min.Y}, {min.Z}) on \"{p.Block.Grid.DisplayName}\""
                : $"Can't place here: {p.Reason}";
        }
        else
        {
            scene.ShowGhost(null, null, false);
            HoverText = SelectedItem is null ? "Choose a block in the list." : string.Empty;
        }
    }

    private (Block, BlockDefinition, bool, string)? ComputePreview(Blueprint blueprint)
    {
        if (SelectedItem?.Definition is not { } def || _ray is not { } ray)
            return null;
        var rotatedSize = BlockGeometry.RotatedSize(def.Size, Orientation);

        CubeGrid grid;
        Vector3I min;
        if (Tool == EditTool.PlaceOnFace)
        {
            if (RaycastAllGrids(ray) is not { } hit)
                return null;
            grid = hit.Grid;
            min = GridOccupancy.MinAgainstFace(hit.Hit, rotatedSize);
        }
        else
        {
            if (PlaneGrid is not { } planeGrid || ToGridCells(planeGrid, ray) is not { } local ||
                GridOccupancy.CellOnPlane(local.Origin, local.Direction, PlaneAxis, PlaneLayer) is not { } cell)
                return null;
            grid = planeGrid;
            min = GridOccupancy.MinOnPlane(cell, PlaneAxis, rotatedSize);
        }

        var block = BlockFactory.Create(grid, def, min, Orientation, _main.Paint.ColorMask, assignEntityId: false);
        if (def.CubeSize != grid.GridSize)
            return (block, def, false, $"a {def.CubeSize.ToString().ToLowerInvariant()}-grid block can't go on a {grid.GridSize.ToString().ToLowerInvariant()} grid");
        if (!Occupancy(grid).IsFree(min, def.Size, Orientation))
            return (block, def, false, "another block is in the way");
        return (block, def, true, string.Empty);
    }

    /// <summary>The nearest block face under the ray, over every grid.</summary>
    private (CubeGrid Grid, CellHit Hit)? RaycastAllGrids((Vector3 Origin, Vector3 Direction) ray)
    {
        (CubeGrid, CellHit)? best = null;
        foreach (var grid in _main.Blueprint?.Grids ?? new List<CubeGrid>())
        {
            if (ToGridCells(grid, ray) is not { } local || Occupancy(grid).Raycast(local.Origin, local.Direction) is not { } hit)
                continue;
            // The ray keeps its parameter t through a rigid transform, so distances compare across grids.
            if (best is null || hit.Distance < best.Value.Item2.Distance)
                best = (grid, hit);
        }
        return best;
    }

    /// <summary>The scene-space ray in a grid's cell units (cell (x,y,z) centered at (x,y,z)).</summary>
    private (Vector3 Origin, Vector3 Direction)? ToGridCells(CubeGrid grid, (Vector3 Origin, Vector3 Direction) ray)
    {
        if (_main.Scene?.GridToScene(grid) is not { } toScene || !Matrix4x4.Invert(toScene, out var toGrid))
            return null;
        var cell = grid.CellSize;
        return (Vector3.Transform(ray.Origin, toGrid) / cell, Vector3.TransformNormal(ray.Direction, toGrid) / cell);
    }

    private GridOccupancy Occupancy(CubeGrid grid)
    {
        if (!_occupancy.TryGetValue(grid, out var occ))
        {
            var defs = _main.Definitions;
            _occupancy[grid] = occ = new GridOccupancy(grid, b => defs.Get(b).Size);
        }
        return occ;
    }

    private void PickFrom(Block block)
    {
        var def = _main.Definitions.Get(block);
        if (def.IsUnknown)
        {
            _main.StatusText = "That block has no definition (mod block?), so it can't be placed.";
            return;
        }
        PaletteSize = def.CubeSize;
        Search = string.Empty;
        SelectedItem = Palette.FirstOrDefault(i => i.Definition == def);
        Orientation = block.Orientation;
        _main.Paint.SetFromMask(block.ColorMaskHsv);
        _main.StatusText = $"Placing {def.DisplayName}.";
    }

    // ───────────────────────────── Work plane ─────────────────────────────

    /// <summary>Puts the plane on the selected block's grid and layer, or the middle of the main grid.</summary>
    private void ResetPlane()
    {
        if (_main.SelectedBlocks.Count > 0)
            PlaneGrid = _main.SelectedBlocks[0].Grid;
        else if (PlaneGrid is null || _main.Blueprint?.Grids.Contains(PlaneGrid) != true)
            PlaneGrid = _main.Blueprint is { } bp ? MainGrid(bp) : null;
        ResetPlaneLayer();
    }

    private void ResetPlaneLayer()
    {
        if (_main.SelectedBlocks.Count > 0 && _main.SelectedBlocks[0].Grid == PlaneGrid)
        {
            PlaneLayer = Along(_main.SelectedBlocks[0].Min, PlaneAxis);
        }
        else if (PlaneGrid is { } grid)
        {
            var occ = Occupancy(grid);
            PlaneLayer = (Along(occ.Min, PlaneAxis) + Along(occ.Max, PlaneAxis)) / 2;
        }
    }

    private static int Along(Vector3I v, PlaneAxis axis) => axis switch { PlaneAxis.X => v.X, PlaneAxis.Y => v.Y, _ => v.Z };

    private static CubeGrid? MainGrid(Blueprint blueprint) => blueprint.Grids.OrderByDescending(g => g.Blocks.Count).FirstOrDefault();

    // ───────────────────────────── Palette ─────────────────────────────

    private void RefreshPalette()
    {
        var defs = _main.Definitions;
        if (ReferenceEquals(defs, _paletteSource))
            return;
        _paletteSource = defs;
        _allItems = defs.All
            .Where(d => !d.IsUnknown)
            .OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.SubtypeId, StringComparer.Ordinal)
            .Select(d => new PaletteItem(d))
            .ToList();
        FilterPalette();
    }

    private void FilterPalette()
    {
        var keep = SelectedItem;
        var words = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Palette.Clear();
        foreach (var item in _allItems)
        {
            if (item.Definition.CubeSize != PaletteSize)
                continue;
            if (words.All(w => item.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase) ||
                               item.Definition.SubtypeId.Contains(w, StringComparison.OrdinalIgnoreCase)))
                Palette.Add(item);
        }
        // Keep the chosen block even if the search hides it (clearing the list may have reset the selection).
        SelectedItem = keep;
    }
}
