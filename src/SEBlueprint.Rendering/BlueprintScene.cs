using System.Numerics;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using SEBlueprint.Core.Colors;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Editing;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;
using SEBlueprint.Core.Models;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;

namespace SEBlueprint.Rendering;

/// <summary>How thick block outlines are drawn, in pixels.</summary>
public enum OutlineWidth
{
    Thin = 1,
    Medium = 2,
    Thick = 3,
}

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

    /// <summary>Edge lines per armor mesh, shared by every scene (built once per shape).</summary>
    private static readonly Dictionary<MeshData, LineGeometry3D?> EdgeLines = new();

    /// <summary>Block outlines: dark, thin, drawn on top of the surface they lie on.</summary>
    private static readonly System.Windows.Media.Color OutlineColor = System.Windows.Media.Color.FromRgb(16, 16, 16);

    /// <summary>Selection outlines are drawn slightly larger than the block so they aren't hidden inside it.</summary>
    private static readonly Matrix4x4 OutlineGrow = Matrix4x4.CreateScale(1.04f);

    private readonly DefinitionRepository _definitions;
    private readonly BlockModelLibrary _models;
    private readonly Dictionary<CubeGrid, GridEntry> _grids = new();
    private readonly Dictionary<Element3D, (GridEntry Grid, SceneBatch Batch)> _batchByModel = new();
    private readonly LineGeometryModel3D _selectionOutline;
    private readonly LineGeometryModel3D _ghostOutline;
    private readonly LineGeometryModel3D _workPlane;
    private readonly List<InstancingMeshGeometryModel3D> _ghostModels = new();
    private string? _ghostKey;
    private IReadOnlyCollection<Block> _selection = Array.Empty<Block>();
    private Func<Block, Vector4>? _colorOverride;
    private bool _showOutlines;
    private OutlineWidth _outlineWidth = OutlineWidth.Thin;

    /// <summary>Line thickness of block outlines. Changing it doesn't rebuild anything.</summary>
    public OutlineWidth OutlineWidth
    {
        get => _outlineWidth;
        set
        {
            _outlineWidth = value;
            foreach (var entry in _grids.Values)
            {
                foreach (var line in entry.Models.OfType<LineMaterialGeometryModel3D>())
                {
                    if (line.Material is LineMaterial material)
                        StyleOutline(material, entry.Data.Grid.CellSize, value);
                }
            }
        }
    }

    /// <summary>
    /// Draws the edges of every armor block (and plain box) so single blocks stand out in a wall of armor.
    /// Real models (thrusters, turrets...) aren't outlined; they're already easy to tell apart.
    /// </summary>
    public bool ShowOutlines
    {
        get => _showOutlines;
        set
        {
            if (value == _showOutlines)
                return;
            _showOutlines = value;
            foreach (var entry in _grids.Values)
            {
                RemoveModels(entry);
                AddModels(entry);
            }
        }
    }

    /// <summary>
    /// Recolors every block with a rule (RGBA 0..1) instead of its paint, for analysis views such as heat maps.
    /// Textures are turned off so the colors read clearly. Alpha below 1 makes blocks see-through, and blocks that
    /// are mostly transparent can't be clicked (so you can click through ghosted armor). Null restores normal colors.
    /// </summary>
    public void SetColorOverride(Func<Block, Vector4>? colorOf)
    {
        _colorOverride = colorOf;
        foreach (var entry in _grids.Values)
        {
            RemoveModels(entry);
            AddModels(entry);
        }
        ShowSelection(_selection);
    }

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

        _ghostOutline = new LineGeometryModel3D
        {
            Geometry = UnitCubeOutline,
            Thickness = 2,
            DepthBias = -100,
            IsHitTestVisible = false,
            Visibility = System.Windows.Visibility.Collapsed,
        };
        _workPlane = new LineGeometryModel3D
        {
            Color = System.Windows.Media.Color.FromRgb(120, 200, 255),
            Thickness = 1,
            IsHitTestVisible = false,
            Visibility = System.Windows.Visibility.Collapsed,
        };
        Root.Children.Add(_workPlane);
        Root.Children.Add(_ghostOutline);
    }

    /// <summary>Transform from a grid's local space (meters) to the scene, or null if the grid isn't drawn.</summary>
    public Matrix4x4? GridToScene(CubeGrid grid) => _grids.TryGetValue(grid, out var entry) ? entry.Data.GridToScene : null;

    /// <summary>
    /// Shows a see-through preview of a block that isn't in its grid yet (null hides it), with a green box where it
    /// would go, or red if it can't go there.
    /// </summary>
    public void ShowGhost(Block? ghost, BlockDefinition? definition, bool valid)
    {
        if (ghost is null || definition is null || !_grids.TryGetValue(ghost.Grid, out var entry))
        {
            SetGhostVisible(false);
            return;
        }

        var visual = _models.Get(definition);
        if (visual.Key != _ghostKey)
        {
            foreach (var old in _ghostModels)
                Root.Children.Remove(old);
            _ghostModels.Clear();
            foreach (var part in visual.Parts)
            {
                var model = new InstancingMeshGeometryModel3D
                {
                    Geometry = GpuMesh(part.Mesh),
                    IsTransparent = true,
                    IsHitTestVisible = false,
                    CullMode = SharpDX.Direct3D11.CullMode.Back,
                    Material = new PhongMaterial
                    {
                        DiffuseColor = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f),
                        AmbientColor = new HelixToolkit.Maths.Color4(0.4f, 0.4f, 0.4f, 1f),
                    },
                };
                _ghostModels.Add(model);
                Root.Children.Add(model);
            }
            _ghostKey = visual.Key;
        }

        var gridToScene = entry.Data.GridToScene;
        var transform = new[] { SceneBuilder.VisualTransform(ghost, definition, visual) * gridToScene };
        var rgb = valid ? SeColor.MaskToRgb(ghost.ColorMaskHsv) : new Vector3(1f, 0.2f, 0.2f);
        var parameters = new[] { new InstanceParameter { DiffuseColor = new HelixToolkit.Maths.Color4(rgb.X, rgb.Y, rgb.Z, 0.5f) } };
        foreach (var model in _ghostModels)
        {
            model.Instances = transform;
            model.InstanceParamArray = parameters;
        }

        _ghostOutline.Color = valid ? System.Windows.Media.Color.FromRgb(80, 255, 120) : System.Windows.Media.Color.FromRgb(255, 70, 70);
        _ghostOutline.Instances = new[] { OutlineGrow * SceneBuilder.BoxTransform(ghost, definition) * gridToScene };
        SetGhostVisible(true);
    }

    private void SetGhostVisible(bool visible)
    {
        var v = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        _ghostOutline.Visibility = v;
        foreach (var model in _ghostModels)
            model.Visibility = v;
    }

    /// <summary>
    /// Draws the work plane: a grid of cell lines on the − side (the "floor") of layer <paramref name="layer"/>,
    /// covering the occupied area <paramref name="min"/>..<paramref name="max"/> plus a margin. Null grid hides it.
    /// </summary>
    public void ShowWorkPlane(CubeGrid? grid, PlaneAxis axis, int layer, Vector3I min, Vector3I max)
    {
        if (grid is null || !_grids.TryGetValue(grid, out var entry))
        {
            _workPlane.Visibility = System.Windows.Visibility.Collapsed;
            return;
        }

        const int margin = 4;
        int a = (int)axis, u = (a + 1) % 3, v = (a + 2) % 3;
        int Get(Vector3I p, int i) => i switch { 0 => p.X, 1 => p.Y, _ => p.Z };
        int uMin = Get(min, u) - margin, uMax = Get(max, u) + margin;
        int vMin = Get(min, v) - margin, vMax = Get(max, v) + margin;

        var cell = grid.CellSize;
        var toScene = entry.Data.GridToScene;
        Vector3 Point(float pu, float pv)
        {
            var c = new float[3];
            c[a] = layer - 0.5f;
            c[u] = pu;
            c[v] = pv;
            return Vector3.Transform(new Vector3(c[0], c[1], c[2]) * cell, toScene);
        }

        var points = new List<Vector3>();
        for (var i = uMin; i <= uMax + 1; i++)
        {
            points.Add(Point(i - 0.5f, vMin - 0.5f));
            points.Add(Point(i - 0.5f, vMax + 0.5f));
        }
        for (var j = vMin; j <= vMax + 1; j++)
        {
            points.Add(Point(uMin - 0.5f, j - 0.5f));
            points.Add(Point(uMax + 0.5f, j - 0.5f));
        }

        _workPlane.Geometry = new LineGeometry3D
        {
            Positions = new HelixToolkit.Vector3Collection(points),
            Indices = new HelixToolkit.IntCollection(Enumerable.Range(0, points.Count)),
        };
        _workPlane.Visibility = System.Windows.Visibility.Visible;
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
        foreach (var fullBatch in entry.Data.Batches)
        {
            var batch = fullBatch;
            InstancingMeshGeometryModel3D model;
            if (_colorOverride is null)
            {
                model = CreateBatchModel(batch);
            }
            else
            {
                // Blocks the rule makes fully transparent are left out entirely: drawing them invisibly still costs
                // a see-through pass over every triangle (that halved the frame rate on large ships).
                batch = VisibleOnly(entry, batch, _colorOverride, out var colors);
                if (batch.BlockIndices.Length == 0)
                    continue;
                model = CreateOverrideModel(batch, colors);
            }
            entry.Models.Add(model);
            _batchByModel[model] = (entry, batch);
            AddBeforeSelection(model);

            // Outline opaque blocks only: lines over see-through (ghosted) armor would just be clutter.
            if (_showOutlines && batch.Outlined && !model.IsTransparent && EdgeGeometry(batch.Part.Mesh) is { } edges)
            {
                var material = new LineMaterial { Color = OutlineColor };
                StyleOutline(material, entry.Data.Grid.CellSize, _outlineWidth);
                var outline = new LineMaterialGeometryModel3D
                {
                    Geometry = edges,
                    Instances = batch.Transforms,
                    Material = material,
                    DepthBias = -20, // the lines lie exactly on the faces; pull them forward so they aren't hidden
                    IsHitTestVisible = false,
                };
                entry.Models.Add(outline);
                AddBeforeSelection(outline);
            }
        }
    }

    /// <summary>
    /// Roughly how many pixels wide one meter looks at one meter's distance, with the default 45° camera on a
    /// typical ~1500 px wide viewport. A block of size s at distance d is about PixelsPerMeter × s / d pixels.
    /// </summary>
    private const float PixelsPerMeter = 1800f;

    /// <summary>
    /// Outlines are a fixed number of pixels wide, so from far away they would cover the blocks. They fade out
    /// as blocks shrink on screen: fully drawn while a block is at least 40 line widths across, gone at 12.
    /// Thicker lines and smaller (small-grid) blocks therefore fade sooner.
    /// </summary>
    private static void StyleOutline(LineMaterial material, float cellSize, OutlineWidth width)
    {
        var pixels = (float)(int)width;
        material.Thickness = pixels;
        material.EnableDistanceFading = true;
        material.FadingNearDistance = PixelsPerMeter * cellSize / (40f * pixels);
        material.FadingFarDistance = PixelsPerMeter * cellSize / (12f * pixels);
    }

    /// <summary>Inserts before the selection outline so that stays last (drawn on top).</summary>
    private void AddBeforeSelection(Element3D model) =>
        Root.Children.Insert(Math.Max(0, Root.Children.Count - (_selectionOutline is null ? 0 : 1)), model);

    private static LineGeometry3D? EdgeGeometry(MeshData mesh)
    {
        if (!EdgeLines.TryGetValue(mesh, out var lines))
        {
            var points = MeshEdges.FeatureEdges(mesh);
            lines = points.Length == 0
                ? null
                : new LineGeometry3D
                {
                    Positions = new HelixToolkit.Vector3Collection(points),
                    Indices = new HelixToolkit.IntCollection(Enumerable.Range(0, points.Length)),
                };
            EdgeLines[mesh] = lines;
        }
        return lines;
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

    private InstancingMeshGeometryModel3D CreateBatchModel(SceneBatch batch)
    {
        var look = batch.Part.Appearance;
        var parameters = new InstanceParameter[batch.Colors.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var c = batch.Colors[i];
            var color = new HelixToolkit.Maths.Color4(c.X, c.Y, c.Z, c.W);
            parameters[i] = look.Kind == PartKind.Emissive
                ? new InstanceParameter { DiffuseColor = color, EmissiveColor = new HelixToolkit.Maths.Color4(0.25f, 0.25f, 0.25f, 1f) }
                : new InstanceParameter { DiffuseColor = color };
            if (batch.TexOffsets is { } offsets)
                parameters[i].TexCoordOffset = offsets[i];
        }

        var texture = batch.Textured && _models.ContentFolder is { } content
            ? TextureCache.Get(content, batch.Part.Textures!.ColorMetal)
            : null;

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
                DiffuseMap = texture,
            },
        };
    }

    /// <summary>The part of a batch whose blocks the color rule doesn't hide (alpha 0), with their colors.</summary>
    private static SceneBatch VisibleOnly(GridEntry entry, SceneBatch batch, Func<Block, Vector4> colorOf, out Vector4[] colors)
    {
        var all = new Vector4[batch.BlockIndices.Length];
        var visible = 0;
        for (var i = 0; i < all.Length; i++)
        {
            all[i] = colorOf(entry.Data.Blocks[batch.BlockIndices[i]]);
            if (all[i].W > 0.001f)
                visible++;
        }
        if (visible == all.Length)
        {
            colors = all;
            return batch;
        }

        var indices = new int[visible];
        var transforms = new Matrix4x4[visible];
        colors = new Vector4[visible];
        for (int i = 0, k = 0; i < all.Length; i++)
        {
            if (all[i].W <= 0.001f)
                continue;
            indices[k] = batch.BlockIndices[i];
            transforms[k] = batch.Transforms[i];
            colors[k] = all[i];
            k++;
        }
        return new SceneBatch
        {
            Part = batch.Part,
            BlockIndices = indices,
            Transforms = transforms,
            Colors = colors,
            Textured = false,
            Outlined = batch.Outlined,
        };
    }

    /// <summary>A batch drawn with colors from an analysis rule instead of its paint and textures.</summary>
    private static InstancingMeshGeometryModel3D CreateOverrideModel(SceneBatch batch, Vector4[] colors)
    {
        var parameters = new InstanceParameter[batch.BlockIndices.Length];
        var anyTransparent = false;
        var allGhosted = true;
        for (var i = 0; i < parameters.Length; i++)
        {
            var c = colors[i];
            parameters[i] = new InstanceParameter { DiffuseColor = new HelixToolkit.Maths.Color4(c.X, c.Y, c.Z, c.W) };
            anyTransparent |= c.W < 0.999f;
            allGhosted &= c.W < 0.5f;
        }

        return new InstancingMeshGeometryModel3D
        {
            Geometry = GpuMesh(batch.Part.Mesh),
            Instances = batch.Transforms,
            InstanceParamArray = parameters,
            IsTransparent = anyTransparent,
            IsHitTestVisible = !allGhosted,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
            Material = new PhongMaterial
            {
                DiffuseColor = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f),
                AmbientColor = new HelixToolkit.Maths.Color4(0.35f, 0.35f, 0.35f, 1f),
                SpecularColor = new HelixToolkit.Maths.Color4(0.1f, 0.1f, 0.1f, 1f),
                SpecularShininess = 20f,
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
            if (mesh.TexCoords is { } uv)
                gpu.TextureCoordinates = new HelixToolkit.Vector2Collection(uv);
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
