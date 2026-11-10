using System.Numerics;
using HelixToolkit.SharpDX;
using MeshBuilder = HelixToolkit.Geometry.MeshBuilder;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using HelixToolkit.Wpf.SharpDX;
using SEBlueprint.Core.Model;
using Media3D = System.Windows.Media.Media3D;

namespace SEBlueprint.Rendering;

/// <summary>
/// The 3D models for a loaded blueprint: one instanced cube model per grid, plus a selection outline.
/// Add <see cref="Root"/> to a Viewport3DX. Must be created and used on the UI thread.
/// </summary>
public sealed class BlueprintScene
{
    private static readonly MeshGeometry3D UnitCube = CreateUnitCube();
    private static readonly LineGeometry3D UnitCubeOutline = LineBuilder.GenerateBoundingBox(
        new HelixToolkit.Maths.BoundingBox(new Vector3(-0.5f), new Vector3(0.5f)));

    private readonly Dictionary<Element3D, GridSceneData> _gridByModel = new();
    private readonly LineGeometryModel3D _selectionOutline;

    public BlueprintScene(BlueprintSceneData data)
    {
        Data = data;
        Root = new GroupModel3D();

        foreach (var grid in data.Grids)
        {
            if (grid.Blocks.Length == 0)
                continue;
            var model = CreateGridModel(grid);
            _gridByModel[model] = grid;
            Root.Children.Add(model);
        }

        _selectionOutline = new LineGeometryModel3D
        {
            Geometry = UnitCubeOutline,
            Color = System.Windows.Media.Colors.Yellow,
            Thickness = 2.5,
            DepthBias = -100,
            IsHitTestVisible = false,
            Visibility = System.Windows.Visibility.Collapsed,
        };
        Root.Children.Add(_selectionOutline);
    }

    public BlueprintSceneData Data { get; }

    /// <summary>The node to put into the viewport.</summary>
    public GroupModel3D Root { get; }

    /// <summary>
    /// Works out which block a 3D hit landed on. Returns null if the hit wasn't on one of our blocks.
    /// </summary>
    public Block? BlockFromHit(HitTestResult hit)
    {
        if (hit.ModelHit is not Element3D element || !_gridByModel.TryGetValue(element, out var grid))
            return null;
        // For instanced models, Helix puts the index of the instance that was hit in Tag.
        return hit.Tag is int index && index >= 0 && index < grid.Blocks.Length ? grid.Blocks[index] : null;
    }

    /// <summary>Draws a yellow outline around the block, or hides the outline when null.</summary>
    public void ShowSelection(Block? block)
    {
        var grid = block is null ? null : Data.Grids.FirstOrDefault(g => g.Grid == block.Grid);
        var index = grid?.IndexOf(block!) ?? -1;
        if (grid is null || index < 0)
        {
            _selectionOutline.Visibility = System.Windows.Visibility.Collapsed;
            return;
        }

        // Outline slightly larger than the drawn block so it isn't hidden inside it.
        var grow = Matrix4x4.CreateScale(1.04f);
        _selectionOutline.Transform = new Media3D.MatrixTransform3D(ToMatrix3D(grow * grid.Transforms[index]));
        _selectionOutline.Visibility = System.Windows.Visibility.Visible;
    }

    private static InstancingMeshGeometryModel3D CreateGridModel(GridSceneData grid)
    {
        var parameters = new InstanceParameter[grid.Colors.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var c = grid.Colors[i];
            parameters[i] = new InstanceParameter { DiffuseColor = new HelixToolkit.Maths.Color4(c.X, c.Y, c.Z, 1f) };
        }

        return new InstancingMeshGeometryModel3D
        {
            Geometry = UnitCube,
            Instances = grid.Transforms,
            InstanceParamArray = parameters,
            Material = new PhongMaterial
            {
                DiffuseColor = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f),
                AmbientColor = new HelixToolkit.Maths.Color4(0.25f, 0.25f, 0.25f, 1f),
                SpecularColor = new HelixToolkit.Maths.Color4(0.15f, 0.15f, 0.15f, 1f),
                SpecularShininess = 20f,
            },
        };
    }

    private static MeshGeometry3D CreateUnitCube()
    {
        var builder = new MeshBuilder(generateNormals: true, generateTexCoords: false);
        builder.AddBox(Vector3.Zero, 1f, 1f, 1f);
        return builder.ToMeshGeometry3D();
    }

    private static Media3D.Matrix3D ToMatrix3D(Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
}
