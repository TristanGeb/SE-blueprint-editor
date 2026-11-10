using System.Numerics;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Model;
using SEBlueprint.Core.Models;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;

namespace SEBlueprint.Rendering;

/// <summary>
/// The 3D models for a loaded blueprint: per grid, one instanced model per batch (distinct mesh), plus
/// selection outlines. Add <see cref="Root"/> to a Viewport3DX. Must be created and used on the UI thread.
/// </summary>
public sealed class BlueprintScene
{
    private static readonly LineGeometry3D UnitCubeOutline = LineBuilder.GenerateBoundingBox(
        new HelixToolkit.Maths.BoundingBox(new Vector3(-0.5f), new Vector3(0.5f)));

    /// <summary>GPU meshes are shared by every scene and grid that uses the same block type.</summary>
    private static readonly Dictionary<MeshData, MeshGeometry3D> GpuMeshes = new();

    /// <summary>Selection outlines are drawn slightly larger than the block so they aren't hidden inside it.</summary>
    private static readonly Matrix4x4 OutlineGrow = Matrix4x4.CreateScale(1.04f);

    private readonly DefinitionRepository _definitions;
    private readonly BlockModelLibrary _models;
    private readonly Dictionary<CubeGrid, GridEntry> _grids = new();
    private readonly Dictionary<Element3D, (GridEntry Grid, SceneBatch Batch)> _batchByModel = new();
    private readonly LineGeometryModel3D _selectionOutline;
    private IReadOnlyCollection<Block> _selection = Array.Empty<Block>();

    public BlueprintScene(BlueprintSceneData data, DefinitionRepository definitions, BlockModelLibrary models)
    {
        _definitions = definitions;
        _models = models;
        Root = new GroupModel3D();

        foreach (var gridData in data.Grids)
        {
            var entry = new GridEntry(gridData);
            _grids[gridData.Grid] = entry;
            AddModels(entry);
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

    /// <summary>Total triangles drawn, for the status bar.</summary>
    public long TriangleCount => _grids.Values
        .SelectMany(g => g.Data.Batches)
        .Sum(b => (long)b.Part.Mesh.TriangleCount * b.Transforms.Length);

    /// <summary>Works out which block a 3D hit landed on. Returns null if the hit wasn't on one of our blocks.</summary>
    public Block? BlockFromHit(HitTestResult hit)
    {
        if (hit.ModelHit is not Element3D element || !_batchByModel.TryGetValue(element, out var found))
            return null;
        // For instanced models, Helix puts the index of the instance that was hit in Tag.
        var indices = found.Batch.BlockIndices;
        return hit.Tag is int i && i >= 0 && i < indices.Length ? found.Grid.Data.Blocks[indices[i]] : null;
    }

    /// <summary>Redraws the given grids after an edit (paint, delete, undo...).</summary>
    public void UpdateGrids(IEnumerable<CubeGrid> grids)
    {
        foreach (var grid in grids)
        {
            if (!_grids.TryGetValue(grid, out var entry))
                continue;
            RemoveModels(entry);
            entry.Data = SceneBuilder.BuildGrid(grid, _definitions, _models, entry.Data.GridToScene);
            AddModels(entry);
        }
        ShowSelection(_selection);
    }

    /// <summary>Draws a yellow box around each block; an empty collection hides the outlines.</summary>
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
                outlines.Add(OutlineGrow * entry.Data.BoxTransforms[index]);
        }

        _selectionOutline.Instances = outlines;
        _selectionOutline.Visibility = outlines.Count > 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private void AddModels(GridEntry entry)
    {
        foreach (var batch in entry.Data.Batches)
        {
            var model = CreateBatchModel(batch);
            entry.Models.Add(model);
            _batchByModel[model] = (entry, batch);
            // Insert before the selection outline so it stays last (drawn on top).
            Root.Children.Insert(Math.Max(0, Root.Children.Count - (_selectionOutline is null ? 0 : 1)), model);
        }
    }

    private void RemoveModels(GridEntry entry)
    {
        foreach (var model in entry.Models)
        {
            Root.Children.Remove(model);
            _batchByModel.Remove(model);
        }
        entry.Models.Clear();
    }

    private static InstancingMeshGeometryModel3D CreateBatchModel(SceneBatch batch)
    {
        var look = batch.Part.Appearance;
        var parameters = new InstanceParameter[batch.Colors.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var c = batch.Colors[i];
            var color = new HelixToolkit.Maths.Color4(c.X, c.Y, c.Z, c.W);
            parameters[i] = look.Kind == PartKind.Emissive
                ? new InstanceParameter { DiffuseColor = color, EmissiveColor = new HelixToolkit.Maths.Color4(c.X * 0.6f, c.Y * 0.6f, c.Z * 0.6f, 1f) }
                : new InstanceParameter { DiffuseColor = color };
        }

        var shiny = look.Kind == PartKind.Fixed && look.Color.X > 0.6f;
        return new InstancingMeshGeometryModel3D
        {
            Geometry = GpuMesh(batch.Part.Mesh),
            Instances = batch.Transforms,
            InstanceParamArray = parameters,
            IsTransparent = look.Kind == PartKind.Glass,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
            Material = new PhongMaterial
            {
                DiffuseColor = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f),
                AmbientColor = new HelixToolkit.Maths.Color4(0.3f, 0.3f, 0.3f, 1f),
                SpecularColor = shiny
                    ? new HelixToolkit.Maths.Color4(0.6f, 0.6f, 0.6f, 1f)
                    : new HelixToolkit.Maths.Color4(0.12f, 0.12f, 0.12f, 1f),
                SpecularShininess = shiny ? 60f : 20f,
            },
        };
    }

    private static MeshGeometry3D GpuMesh(MeshData mesh)
    {
        if (!GpuMeshes.TryGetValue(mesh, out var gpu))
        {
            gpu = new MeshGeometry3D
            {
                Positions = new HelixToolkit.Vector3Collection(mesh.Positions),
                Normals = new HelixToolkit.Vector3Collection(mesh.Normals),
                Indices = new HelixToolkit.IntCollection(mesh.Indices),
            };
            GpuMeshes[mesh] = gpu;
        }
        return gpu;
    }

    private sealed class GridEntry(GridSceneData data)
    {
        public GridSceneData Data { get; set; } = data;

        public List<Element3D> Models { get; } = new();
    }
}
