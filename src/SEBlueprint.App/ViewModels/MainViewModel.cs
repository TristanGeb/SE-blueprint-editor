using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SEBlueprint.App.Services;
using SEBlueprint.Core;
using SEBlueprint.Core.Colors;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Geometry;
using SEBlueprint.Core.IO;
using SEBlueprint.Core.Model;
using SEBlueprint.Rendering;

namespace SEBlueprint.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings = AppSettings.Load();
    private Task<DefinitionRepository>? _definitionsTask;
    private Dictionary<Block, BlockNodeViewModel> _nodeByBlock = new();
    private bool _syncingSelection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private Blueprint? _blueprint;

    /// <summary>The 3D scene for the current blueprint. The window puts it into the viewport.</summary>
    [ObservableProperty]
    private BlueprintScene? _scene;

    [ObservableProperty]
    private Block? _selectedBlock;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBlueprintCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ready.";

    public ObservableCollection<TreeNodeViewModel> TreeRoots { get; } = new();

    public ObservableCollection<PropertyRow> SelectedProperties { get; } = new();

    public string WindowTitle => Blueprint is null
        ? "SE Blueprint Editor"
        : $"{Blueprint.Name} - SE Blueprint Editor";

    public DefinitionRepository Definitions { get; private set; } = DefinitionRepository.Empty;

    /// <summary>Called once when the window opens: finds the game and loads block definitions in the background.</summary>
    public async Task InitializeAsync(string? blueprintToOpen)
    {
        var gameFolder = _settings.GameFolder;
        if (!GamePaths.IsGameFolder(gameFolder))
            gameFolder = GamePaths.FindGameFolder() ?? AskForGameFolder();

        if (GamePaths.IsGameFolder(gameFolder))
        {
            _settings.GameFolder = gameFolder;
            _settings.Save();
            StatusText = "Loading block definitions…";
            _definitionsTask = Task.Run(() => DefinitionRepository.LoadFromGame(GamePaths.ContentFolder(gameFolder!)));
            try
            {
                Definitions = await _definitionsTask;
                StatusText = $"Loaded {Definitions.Count:N0} block definitions. Use File → Open to load a blueprint.";
            }
            catch (Exception ex)
            {
                StatusText = "Could not load block definitions: " + ex.Message;
            }
        }
        else
        {
            StatusText = "Space Engineers not found: blocks will be drawn as 1×1×1 cubes. Set the game folder under File.";
        }

        if (!string.IsNullOrEmpty(blueprintToOpen))
            await OpenPathAsync(blueprintToOpen);
    }

    private bool CanOpen() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenBlueprintAsync()
    {
        var startFolder = Directory.Exists(_settings.LastOpenFolder) ? _settings.LastOpenFolder : GamePaths.LocalBlueprintsFolder;
        var dialog = new OpenFileDialog
        {
            Title = "Open blueprint",
            Filter = "Space Engineers blueprint (bp.sbc)|bp.sbc|All .sbc files (*.sbc)|*.sbc",
            InitialDirectory = Directory.Exists(startFolder) ? startFolder : null,
        };
        if (dialog.ShowDialog() != true)
            return;

        // Next time, start in the folder that contains all the blueprints, not inside this one.
        _settings.LastOpenFolder = Path.GetDirectoryName(Path.GetDirectoryName(dialog.FileName));
        _settings.Save();
        await OpenPathAsync(dialog.FileName);
    }

    [RelayCommand]
    private async Task ChooseGameFolderAsync()
    {
        var folder = AskForGameFolder();
        if (!GamePaths.IsGameFolder(folder))
            return;

        _settings.GameFolder = folder;
        _settings.Save();
        IsBusy = true;
        StatusText = "Loading block definitions…";
        try
        {
            Definitions = await Task.Run(() => DefinitionRepository.LoadFromGame(GamePaths.ContentFolder(folder!)));
            StatusText = $"Loaded {Definitions.Count:N0} block definitions.";
        }
        finally
        {
            IsBusy = false;
        }

        if (Blueprint?.FilePath is { } path)
            await OpenPathAsync(path);
    }

    [RelayCommand]
    private void OpenBlueprintFolder()
    {
        var folder = Blueprint?.FilePath is { } path ? Path.GetDirectoryName(path) : GamePaths.LocalBlueprintsFolder;
        if (Directory.Exists(folder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private static void Exit() => Application.Current.Shutdown();

    /// <summary>Loads a blueprint (file or folder) and builds the tree and 3D scene for it.</summary>
    public async Task OpenPathAsync(string path)
    {
        IsBusy = true;
        StatusText = $"Opening {path}…";
        try
        {
            if (_definitionsTask is not null)
                Definitions = await _definitionsTask; // in case a blueprint is opened before definitions finish

            var defs = Definitions;
            var sw = Stopwatch.StartNew();

            // Parsing, tree building and instance data all happen off the UI thread so the window stays responsive.
            var (blueprint, sceneData, roots) = await Task.Run(() =>
            {
                var bp = BlueprintIO.Load(path);
                var data = SceneBuilder.Build(bp, defs);
                var nodes = bp.Grids
                    .OrderByDescending(g => g.Blocks.Count)
                    .Select(g => new GridNodeViewModel(g, defs))
                    .ToList();
                return (bp, data, nodes);
            });

            SelectedBlock = null;
            Blueprint = blueprint;
            Scene = new BlueprintScene(sceneData); // GPU objects must be created on the UI thread

            TreeRoots.Clear();
            _nodeByBlock = new Dictionary<Block, BlockNodeViewModel>();
            foreach (var root in roots)
            {
                TreeRoots.Add(root);
                foreach (var node in root.Children.SelectMany(c => c.Children).OfType<BlockNodeViewModel>())
                    _nodeByBlock[node.Block] = node;
            }
            if (TreeRoots.Count > 0)
                TreeRoots[0].IsExpanded = true;

            var unknown = blueprint.AllBlocks.Count(b => defs.Get(b).IsUnknown);
            StatusText = $"{blueprint.Name}: {blueprint.Grids.Count} grid(s), {blueprint.TotalBlockCount:N0} blocks"
                + (unknown > 0 ? $", {unknown:N0} unknown (magenta)" : string.Empty)
                + $" — loaded in {sw.ElapsedMilliseconds:N0} ms.   Right-drag: rotate · Middle-drag: pan · Wheel: zoom · Click: select";
        }
        catch (Exception ex)
        {
            StatusText = "Could not open blueprint: " + ex.Message;
            MessageBox.Show(ex.Message, "Could not open blueprint", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Called by the tree when the user selects a row.</summary>
    public void OnTreeSelectionChanged(TreeNodeViewModel? node)
    {
        if (_syncingSelection)
            return;
        if (node is BlockNodeViewModel blockNode)
            SelectedBlock = blockNode.Block;
    }

    /// <summary>Called by the viewport when the user clicks a block (or empty space, with null).</summary>
    public void SelectBlockFromViewport(Block? block)
    {
        SelectedBlock = block;
        if (block is null || !_nodeByBlock.TryGetValue(block, out var node))
            return;

        _syncingSelection = true;
        try
        {
            node.ExpandParents();
            node.IsSelected = true;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    partial void OnSelectedBlockChanged(Block? value)
    {
        Scene?.ShowSelection(value);
        SelectedProperties.Clear();
        if (value is null)
            return;

        var def = Definitions.Get(value);
        var rgb = SeColor.MaskToRgb(value.ColorMaskHsv);
        var hsv = SeColor.MaskToHsv(value.ColorMaskHsv);
        var brush = new SolidColorBrush(Color.FromRgb(ToByte(rgb.X), ToByte(rgb.Y), ToByte(rgb.Z)));
        brush.Freeze();

        SelectedProperties.Add(new PropertyRow("Block", def.DisplayName + (def.IsUnknown ? "  (no definition found)" : string.Empty)));
        if (!string.IsNullOrEmpty(value.CustomName))
            SelectedProperties.Add(new PropertyRow("Name", value.CustomName));
        SelectedProperties.Add(new PropertyRow("Type", value.TypeId));
        SelectedProperties.Add(new PropertyRow("Subtype", value.SubtypeName));
        SelectedProperties.Add(new PropertyRow("Grid", value.Grid.DisplayName));
        SelectedProperties.Add(new PropertyRow("Position (Min)", value.Min.ToString()));
        SelectedProperties.Add(new PropertyRow("Size", BlockGeometry.RotatedSize(def.Size, value.Orientation).ToString()));
        SelectedProperties.Add(new PropertyRow("Facing", $"Forward: {value.Orientation.Forward}, Up: {value.Orientation.Up}"));
        SelectedProperties.Add(new PropertyRow("Color", $"H {hsv.X * 360:0}°  S {hsv.Y * 100:0}%  V {hsv.Z * 100:0}%", brush));
        if (!string.IsNullOrEmpty(value.SkinSubtypeId))
            SelectedProperties.Add(new PropertyRow("Skin", value.SkinSubtypeId));
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);

    private static string? AskForGameFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select your Space Engineers install folder (it contains the 'Content' folder)",
        };
        while (dialog.ShowDialog() == true)
        {
            if (GamePaths.IsGameFolder(dialog.FolderName))
                return dialog.FolderName;
            MessageBox.Show(
                "That folder doesn't look like a Space Engineers install.\nPick the 'SpaceEngineers' folder that contains 'Content'.",
                "Not the game folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return null;
    }
}
