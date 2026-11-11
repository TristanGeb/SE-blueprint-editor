using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using SEBlueprint.App.ViewModels;
using SEBlueprint.Core.Model;

namespace SEBlueprint.App;

/// <summary>
/// The main window. Most logic lives in <see cref="MainViewModel"/>; this code-behind only does
/// the things that need direct access to WPF/Helix controls (3D picking, putting the scene in the viewport).
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly DefaultEffectsManager _effectsManager = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Viewport.EffectsManager = _effectsManager;

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Closed += (_, _) => _effectsManager.Dispose();
        Drop += OnDrop;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_vm.ConfirmDiscardChanges())
            e.Cancel = true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Allow opening a blueprint from the command line: SEBlueprint.App.exe "path\to\bp.sbc"
        var args = Environment.GetCommandLineArgs();
        await _vm.InitializeAsync(args.Length > 1 ? args[1] : null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Scene))
            return;

        SceneHost.Children.Clear();
        if (_vm.Scene is { } scene)
        {
            SceneHost.Children.Add(scene.Root);

            // Only frame the ship when a different blueprint was opened; rebuilding the same one
            // (e.g. after changing model detail) keeps the user's camera.
            if (!ReferenceEquals(scene.Blueprint, _framedBlueprint))
            {
                _framedBlueprint = scene.Blueprint;
                ZoomToFitWhenReady();
            }
        }
    }

    private Blueprint? _framedBlueprint;

    /// <summary>
    /// Frames the ship once the viewport has been laid out and drawn a frame. Doing it immediately
    /// (while the window or viewport still has no size) leaves the camera where it was.
    /// </summary>
    private void ZoomToFitWhenReady()
    {
        var framesLeft = 2;
        void OnRendering(object? sender, EventArgs e)
        {
            if (Viewport.ActualWidth < 1 || Viewport.ActualHeight < 1 || --framesLeft > 0)
                return;
            System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
            ZoomToFit();
        }
        System.Windows.Media.CompositionTarget.Rendering += OnRendering;
    }

    private void OnViewportMouseDown3D(object? sender, RoutedEventArgs e)
    {
        if (e is not MouseDown3DEventArgs args)
            return;
        if (args.OriginalInputEventArgs is MouseButtonEventArgs { ChangedButton: not MouseButton.Left })
            return;

        var block = args.HitTestResult is { } hit ? _vm.Scene?.BlockFromHit(hit) : null;
        var toggle = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _vm.SelectBlockFromViewport(block, toggle);
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _vm.OnTreeSelectionChanged(e.NewValue as TreeNodeViewModel);

    private void OnTreeItemSelected(object sender, RoutedEventArgs e)
    {
        // Scroll the tree to the item when it gets selected from the 3D view.
        if (e.OriginalSource is TreeViewItem item)
            item.BringIntoView();
    }

    private void OnZoomExtents(object sender, RoutedEventArgs e) => ZoomToFit();

    /// <summary>Clicking a row in the analysis list selects that block (and highlights it in 3D).</summary>
    private void OnAnalysisRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ViewModels.Workspaces.AnalysisRow row } &&
            _vm.ActiveWorkspace is ViewModels.Workspaces.AnalyzeWorkspaceViewModel analyze)
            analyze.SelectRowCommand.Execute(row);
    }

    /// <summary>
    /// Points the camera so the whole blueprint is in view, keeping the current viewing angle.
    /// Uses the scene's own bounding box (Helix's ZoomExtents only measures the shared block meshes,
    /// one block at the origin, not where the instanced copies are) and sets the camera directly.
    /// </summary>
    private void ZoomToFit()
    {
        if (_vm.Scene?.Bounds is not { } bounds || Viewport.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera camera)
            return;

        var center = (bounds.Min + bounds.Max) / 2f;

        var look = camera.LookDirection;
        if (look.Length < 1e-6)
            look = new System.Windows.Media.Media3D.Vector3D(-1, -1, -1);
        look.Normalize();

        // Camera axes (forward, right, up) as System.Numerics vectors.
        var forward = new System.Numerics.Vector3((float)look.X, (float)look.Y, (float)look.Z);
        var upHint = new System.Numerics.Vector3((float)camera.UpDirection.X, (float)camera.UpDirection.Y, (float)camera.UpDirection.Z);
        var right = System.Numerics.Vector3.Cross(forward, upHint);
        if (right.LengthSquared() < 1e-8f)
            right = System.Numerics.Vector3.Cross(forward, System.Numerics.Vector3.UnitX);
        right = System.Numerics.Vector3.Normalize(right);
        var up = System.Numerics.Vector3.Cross(right, forward);

        // FieldOfView is horizontal; derive the vertical one from the viewport's shape.
        var aspect = Math.Max(Viewport.ActualWidth, 1) / Math.Max(Viewport.ActualHeight, 1);
        var tanH = Math.Tan(camera.FieldOfView * Math.PI / 360);
        var tanV = tanH / aspect;

        // For every block corner, how far back the camera must be for that corner to be on screen.
        // Using block corners rather than the overall bounding box keeps diagonal views tight.
        double distance = 1;
        foreach (var p in _vm.Scene.BlockCorners)
        {
            var corner = p - center;
            var x = Math.Abs(System.Numerics.Vector3.Dot(corner, right));
            var y = Math.Abs(System.Numerics.Vector3.Dot(corner, up));
            var z = System.Numerics.Vector3.Dot(corner, forward);
            distance = Math.Max(distance, Math.Max(x / tanH, y / tanV) - z);
        }
        distance *= 1.05; // a little margin

        look *= distance; // the camera orbits around Position + LookDirection, i.e. the ship's center

        camera.Position = new System.Windows.Media.Media3D.Point3D(
            center.X - look.X, center.Y - look.Y, center.Z - look.Z);
        camera.LookDirection = look;
        camera.NearPlaneDistance = Math.Max(0.05, distance / 2000);
        camera.FarPlaneDistance = distance * 50;

        // Helix only redraws on its own when the mouse moves the camera; changing it from code needs
        // an explicit redraw, otherwise the new view only appears after the next mouse drag.
        Viewport.InvalidateRender();
    }

    // PreviewKeyDown runs before any control (tree, viewport) can swallow the key.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.None && e.OriginalSource is not TextBox)
        {
            ZoomToFit();
            e.Handled = true;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
            return;

        var path = paths[0];
        if (Directory.Exists(path) || path.EndsWith(".sbc", StringComparison.OrdinalIgnoreCase))
            await _vm.OpenPathAsync(path);
    }
}
