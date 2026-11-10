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
        Blueprint = data.Blueprint;
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

    /// <summary>The blueprint this scene draws.</summary>
    public Blueprint Blueprint { get; }

    /// <summary>
    /// Bounding box of every block in the scene, or null if there are no blocks. Computed from our own data
    /// so "zoom to fit" works even before the GPU has finished setting up the models.
    /// </summary>
    public (Vector3 Min, Vector3 Max)? Bounds
    {
        get
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var any = false;
            foreach (var grid in _grids.Values)
            {
                foreach (var box in grid.Data.BoxTransforms)
                {
                    for (var c = 0; c < 8; c++)
                    {
                        var corner = new Vector3((c & 1) == 0 ? -0.5f : 0.5f, (c & 2) == 0 ? -0.5f : 0.5f, (c & 4) == 0 ? -0.5f : 0.5f);
                        var p = Vector3.Transform(corner, box);
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                    any = true;
                }
            }
            return any ? (min, max) : null;
        }
    }

    /// <summary>The 8 corners of every block's box, for fitting the camera tightly around the actual ship shape.</summary>
    public IEnumerable<Vector3> BlockCorners
    {
        get
        {
            foreach (var grid in _grids.Values)
            {
                foreach (var box in grid.Data.BoxTransforms)
                {
                    for (var c = 0; c < 8; c++)
                    {
                        var corner = new Vector3((c & 1) == 0 ? -0.5f : 0.5f, (c & 2) == 0 ? -0.5f : 0.5f, (c & 4) == 0 ? -0.5f : 0.5f);
                        yield return Vector3.Transform(corner, box);
                    }
                }
            }
        }
    }

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

        // Highlights are white whatever the paint, like the game's metal: they keep the shape readable even on
        // near-black paint (where diffuse lighting alone leaves everything flat black).
        var (specular, shininess) = look.Kind switch
        {
            PartKind.Paint => (0.35f, 24f),
            PartKind.Fixed when look.Color.X > 0.6f => (0.6f, 60f), // chrome
            PartKind.Fixed => (0.25f, 24f),
            _ => (0.12f, 20f),
        };
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
                SpecularColor = new HelixToolkit.Maths.Color4(specular, specular, specular, 1f),
                SpecularShininess = shininess,
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
