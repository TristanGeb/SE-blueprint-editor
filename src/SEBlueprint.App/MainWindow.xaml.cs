using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using SEBlueprint.App.ViewModels;

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
            ZoomToFit(0);
        }
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

    /// <summary>
    /// Points the camera so the whole blueprint is in view. Uses the scene's own bounding box: Helix's
    /// built-in ZoomExtents measures the shared block meshes (one block at the origin), not where the
    /// thousands of instanced copies actually are, so it zoomed onto a single block.
    /// </summary>
    private void ZoomToFit(double animationMs = 300)
    {
        if (_vm.Scene?.Bounds is not { } bounds)
            return;
        var size = bounds.Max - bounds.Min;
        var box = new System.Windows.Media.Media3D.Rect3D(
            bounds.Min.X, bounds.Min.Y, bounds.Min.Z,
            Math.Max(size.X, 0.5), Math.Max(size.Y, 0.5), Math.Max(size.Z, 0.5));
        Viewport.ZoomExtents(box, animationMs);
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
