using System.Numerics;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Model;
using MeshBuilder = HelixToolkit.Geometry.MeshBuilder;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;

namespace SEBlueprint.Rendering;

/// <summary>
/// The 3D models for a loaded blueprint: one instanced cube model per grid, plus selection outlines.
/// Add <see cref="Root"/> to a Viewport3DX. Must be created and used on the UI thread.
/// </summary>
public sealed class BlueprintScene
{
    private static readonly MeshGeometry3D UnitCube = CreateUnitCube();
    private static readonly LineGeometry3D UnitCubeOutline = LineBuilder.GenerateBoundingBox(
        new HelixToolkit.Maths.BoundingBox(new Vector3(-0.5f), new Vector3(0.5f)));

    /// <summary>Selection outlines are drawn slightly larger than the block so they aren't hidden inside it.</summary>
    private static readonly Matrix4x4 OutlineGrow = Matrix4x4.CreateScale(1.04f);

    private readonly DefinitionRepository _definitions;
    private readonly Dictionary<CubeGrid, GridEntry> _grids = new();
    private readonly Dictionary<Element3D, GridEntry> _gridByModel = new();
    private readonly LineGeometryModel3D _selectionOutline;
    private IReadOnlyCollection<Block> _selection = Array.Empty<Block>();

    public BlueprintScene(BlueprintSceneData data, DefinitionRepository definitions)
    {
        _definitions = definitions;
        Root = new GroupModel3D();

        foreach (var gridData in data.Grids)
        {
            var model = CreateGridModel();
            var entry = new GridEntry(model, gridData);
            ApplyData(entry);
            _grids[gridData.Grid] = entry;
            _gridByModel[model] = entry;
            Root.Children.Add(model);
        }

        _selectionOutline = new LineGeometryModel3D
        {
            Geometry = UnitCubeOutline,
            Color = System.Windows.Media.Colors.Yellow,
            Thickness = 2,
            DepthBias = -100,
            IsHitTestVisible = false,
            Visibility = System.Windows.Visibility.Collapsed,
        };
        Root.Children.Add(_selectionOutline);
    }

    /// <summary>The node to put into the viewport.</summary>
    public GroupModel3D Root { get; }

    /// <summary>Works out which block a 3D hit landed on. Returns null if the hit wasn't on one of our blocks.</summary>
    public Block? BlockFromHit(HitTestResult hit)
    {
        if (hit.ModelHit is not Element3D element || !_gridByModel.TryGetValue(element, out var entry))
            return null;
        // For instanced models, Helix puts the index of the instance that was hit in Tag.
        var blocks = entry.Data.Blocks;
        return hit.Tag is int index && index >= 0 && index < blocks.Length ? blocks[index] : null;
    }

    /// <summary>Redraws the given grids after an edit (paint, delete, undo...).</summary>
    public void UpdateGrids(IEnumerable<CubeGrid> grids)
    {
        foreach (var grid in grids)
        {
            if (!_grids.TryGetValue(grid, out var entry))
                continue;
            entry.Data = SceneBuilder.BuildGrid(grid, _definitions, entry.Data.GridToScene);
            ApplyData(entry);
        }
        ShowSelection(_selection);
    }

    /// <summary>Draws a yellow outline around each block; an empty collection hides the outlines.</summary>
    public void ShowSelection(IReadOnlyCollection<Block> blocks)
    {
        _selection = blocks;
        var outlines = new List<Matrix4x4>(blocks.Count);
        foreach (var block in blocks)
        {
            if (!_grids.TryGetValue(block.Grid, out var entry))
                continue;
            var index = entry.Data.IndexOf(block);
            if (index >= 0)
                outlines.Add(OutlineGrow * entry.Data.Transforms[index]);
        }

        _selectionOutline.Instances = outlines;
        _selectionOutline.Visibility = outlines.Count > 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private static void ApplyData(GridEntry entry)
    {
        var data = entry.Data;
        var parameters = new InstanceParameter[data.Colors.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var c = data.Colors[i];
            parameters[i] = new InstanceParameter { DiffuseColor = new HelixToolkit.Maths.Color4(c.X, c.Y, c.Z, 1f) };
        }

        entry.Model.Instances = data.Transforms;
        entry.Model.InstanceParamArray = parameters;
        entry.Model.Visibility = data.Blocks.Length > 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private static InstancingMeshGeometryModel3D CreateGridModel() => new()
    {
        Geometry = UnitCube,
        Material = new PhongMaterial
        {
            DiffuseColor = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f),
            AmbientColor = new HelixToolkit.Maths.Color4(0.25f, 0.25f, 0.25f, 1f),
            SpecularColor = new HelixToolkit.Maths.Color4(0.15f, 0.15f, 0.15f, 1f),
            SpecularShininess = 20f,
        },
    };

    private static MeshGeometry3D CreateUnitCube()
    {
        var builder = new MeshBuilder(generateNormals: true, generateTexCoords: false);
        builder.AddBox(Vector3.Zero, 1f, 1f, 1f);
        return builder.ToMeshGeometry3D();
    }

    private sealed class GridEntry(InstancingMeshGeometryModel3D model, GridSceneData data)
    {
        public InstancingMeshGeometryModel3D Model { get; } = model;

        public GridSceneData Data { get; set; } = data;
    }
}
