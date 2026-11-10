using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SEBlueprint.App.Services;
using SEBlueprint.App.Views;
using SEBlueprint.Core;
using SEBlueprint.Core.Colors;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Editing;
using SEBlueprint.Core.Geometry;
using SEBlueprint.Core.IO;
using SEBlueprint.Core.Model;
using SEBlueprint.Rendering;

namespace SEBlueprint.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const string ViewportHelp = "Right-drag: rotate · Middle-drag: pan · Wheel: zoom · Click: select · Ctrl+click: add/remove";

    private readonly AppSettings _settings = AppSettings.Load();
    private Task<DefinitionRepository>? _definitionsTask;
    private Dictionary<Block, BlockNodeViewModel> _nodeByBlock = new();
    private bool _syncingSelection;

    /// <summary>The open blueprint and its undo history, or null when nothing is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Blueprint), nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(SaveAsNewCommand), nameof(UndoCommand), nameof(RedoCommand))]
    private EditorSession? _session;

    /// <summary>The 3D scene for the current blueprint. The window puts it into the viewport.</summary>
    [ObservableProperty]
    private BlueprintScene? _scene;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PaintSelectedCommand), nameof(DeleteSelectedCommand), nameof(PickColorFromSelectionCommand))]
    private IReadOnlyList<Block> _selectedBlocks = Array.Empty<Block>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBlueprintCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ready.";

    public Blueprint? Blueprint => Session?.Blueprint;

    public ObservableCollection<TreeNodeViewModel> TreeRoots { get; } = new();

    public ObservableCollection<PropertyRow> SelectedProperties { get; } = new();

    public PaintViewModel Paint { get; } = new();

    public string WindowTitle => Session is null
        ? "SE Blueprint Editor"
        : $"{(Session.IsDirty ? "● " : string.Empty)}{Session.Blueprint.Name} - SE Blueprint Editor";

    public string UndoHeader => Session?.UndoName is { } n ? $"_Undo {n}" : "_Undo";

    public string RedoHeader => Session?.RedoName is { } n ? $"_Redo {n}" : "_Redo";

    public DefinitionRepository Definitions { get; private set; } = DefinitionRepository.Empty;

    // ───────────────────────────── Startup ─────────────────────────────

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

    // ───────────────────────────── File ─────────────────────────────

    private bool CanOpen() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenBlueprintAsync()
    {
        if (!ConfirmDiscardChanges())
            return;

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
        await OpenPathAsync(dialog.FileName, confirmed: true);
    }

    private bool HasSession() => Session is not null;

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void Save()
    {
        if (Session is null)
            return;
        try
        {
            Session.Save();
            StatusText = $"Saved {Session.Blueprint.FilePath}. The original is kept as {BlueprintIO.BackupFileName}.";
        }
        catch (Exception ex)
        {
            ShowError("Could not save", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void SaveAsNew()
    {
        if (Session is null)
            return;

        var parent = Session.Blueprint.FilePath is { } p
            ? Path.GetDirectoryName(Path.GetDirectoryName(p))!
            : GamePaths.LocalBlueprintsFolder;
        var dialog = new NameDialog(parent, SuggestCopyName(parent, Session.Blueprint.Name)) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            Session.SaveAsNew(parent, dialog.BlueprintName);
            OnPropertyChanged(nameof(WindowTitle));
            StatusText = $"Saved as new blueprint \"{dialog.BlueprintName}\". It will show up in the game's blueprint list (F10).";
        }
        catch (Exception ex)
        {
            ShowError("Could not save", ex);
        }
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
            _definitionsTask = Task.FromResult(Definitions);
            StatusText = $"Loaded {Definitions.Count:N0} block definitions.";
        }
        finally
        {
            IsBusy = false;
        }

        if (Blueprint?.FilePath is { } path && ConfirmDiscardChanges())
            await OpenPathAsync(path, confirmed: true);
    }

    [RelayCommand]
    private void OpenBlueprintFolder()
    {
        var folder = Blueprint?.FilePath is { } path ? Path.GetDirectoryName(path) : GamePaths.LocalBlueprintsFolder;
        if (Directory.Exists(folder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private static void Exit() => Application.Current.MainWindow?.Close();

    /// <summary>Loads a blueprint (file or folder) and builds the tree and 3D scene for it.</summary>
    public async Task OpenPathAsync(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscardChanges())
            return;

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

            if (Session is not null)
            {
                Session.Changed -= OnSessionChanged;
                Session.HistoryChanged -= OnHistoryChanged;
            }

            SelectedBlocks = Array.Empty<Block>();
            Session = new EditorSession(blueprint);
            Session.Changed += OnSessionChanged;
            Session.HistoryChanged += OnHistoryChanged;
            Scene = new BlueprintScene(sceneData, defs); // GPU objects must be created on the UI thread

            TreeRoots.Clear();
            _nodeByBlock = new Dictionary<Block, BlockNodeViewModel>();
            foreach (var root in roots)
            {
                TreeRoots.Add(root);
                IndexBlockNodes(root);
            }
            if (TreeRoots.Count > 0)
                TreeRoots[0].IsExpanded = true;

            var unknown = blueprint.AllBlocks.Count(b => defs.Get(b).IsUnknown);
            StatusText = $"{blueprint.Name}: {blueprint.Grids.Count} grid(s), {blueprint.TotalBlockCount:N0} blocks"
                + (unknown > 0 ? $", {unknown:N0} unknown (magenta)" : string.Empty)
                + $" — loaded in {sw.ElapsedMilliseconds:N0} ms.   {ViewportHelp}";
        }
        catch (Exception ex)
        {
            ShowError("Could not open blueprint", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Asks whether to save unsaved changes. Returns false if the user cancelled.</summary>
    public bool ConfirmDiscardChanges()
    {
        if (Session is not { IsDirty: true })
            return true;

        var answer = MessageBox.Show(
            $"Save changes to \"{Session.Blueprint.Name}\"?",
            "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
            return false;
        if (answer == MessageBoxResult.Yes)
        {
            Save();
            return !Session.IsDirty; // stay open if saving failed
        }
        return true;
    }

    // ───────────────────────────── Edit ─────────────────────────────

    private bool CanUndo() => Session?.CanUndo == true;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Session?.Undo();

    private bool CanRedo() => Session?.CanRedo == true;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Session?.Redo();

    private bool HasSelection() => SelectedBlocks.Count > 0;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void PaintSelected() => Run(new PaintBlocksCommand(SelectedBlocks, Paint.ColorMask));

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        DeleteBlocksCommand command;
        try
        {
            command = new DeleteBlocksCommand(SelectedBlocks);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Can't delete", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedBlocks = Array.Empty<Block>();
        Run(command);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void PickColorFromSelection() => Paint.SetFromMask(SelectedBlocks[0].ColorMaskHsv);

    [RelayCommand]
    private void ClearSelection() => SelectedBlocks = Array.Empty<Block>();

    private void Run(IEditCommand command)
    {
        try
        {
            Session?.Execute(command);
            StatusText = command.Name + ". Ctrl+Z to undo.";
        }
        catch (Exception ex)
        {
            ShowError("Edit failed", ex);
        }
    }

    private void OnSessionChanged(object? sender, EditChange change)
    {
        Scene?.UpdateGrids(change.Grids);

        if (change.StructureChanged)
        {
            foreach (var grid in change.Grids)
                RebuildGridNode(grid);

            // Drop anything that no longer exists (deleted blocks).
            var alive = change.Grids.SelectMany(g => g.Blocks).ToHashSet();
            var changedGrids = change.Grids.ToHashSet();
            var remaining = SelectedBlocks.Where(b => !changedGrids.Contains(b.Grid) || alive.Contains(b)).ToArray();
            if (remaining.Length != SelectedBlocks.Count)
                SelectedBlocks = remaining;
        }

        RefreshProperties();
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(UndoHeader));
        OnPropertyChanged(nameof(RedoHeader));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    // ───────────────────────────── Selection ─────────────────────────────

    /// <summary>Called by the tree when the user selects a row: selects every block under it.</summary>
    public void OnTreeSelectionChanged(TreeNodeViewModel? node)
    {
        if (_syncingSelection || node is null)
            return;
        SelectedBlocks = node.Blocks.ToArray();
    }

    /// <summary>
    /// Called by the viewport when the user clicks. A plain click selects just that block (or nothing,
    /// on empty space); Ctrl+click adds or removes the block from the selection.
    /// </summary>
    public void SelectBlockFromViewport(Block? block, bool toggle)
    {
        if (toggle)
        {
            if (block is null)
                return;
            SelectedBlocks = SelectedBlocks.Contains(block)
                ? SelectedBlocks.Where(b => b != block).ToArray()
                : SelectedBlocks.Append(block).ToArray();
        }
        else
        {
            SelectedBlocks = block is null ? Array.Empty<Block>() : new[] { block };
        }

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

    partial void OnSelectedBlocksChanged(IReadOnlyList<Block> value)
    {
        Scene?.ShowSelection(value);
        RefreshProperties();
    }

    private void RefreshProperties()
    {
        SelectedProperties.Clear();
        var blocks = SelectedBlocks;
        if (blocks.Count == 0)
            return;

        if (blocks.Count > 1)
        {
            SelectedProperties.Add(new PropertyRow("Selected", $"{blocks.Count:N0} blocks"));
            var types = blocks.GroupBy(b => Definitions.Get(b).DisplayName).OrderByDescending(g => g.Count()).ToList();
            SelectedProperties.Add(new PropertyRow("Block types", types.Count.ToString()));
            foreach (var t in types.Take(6))
                SelectedProperties.Add(new PropertyRow("", $"{t.Count():N0} × {t.Key}"));
            if (types.Count > 6)
                SelectedProperties.Add(new PropertyRow("", $"… and {types.Count - 6} more types"));
            var grids = blocks.Select(b => b.Grid).Distinct().Count();
            if (grids > 1)
                SelectedProperties.Add(new PropertyRow("Grids", grids.ToString()));
            return;
        }

        var block = blocks[0];
        var def = Definitions.Get(block);
        var hsv = SeColor.MaskToHsv(block.ColorMaskHsv);

        SelectedProperties.Add(new PropertyRow("Block", def.DisplayName + (def.IsUnknown ? "  (no definition found)" : string.Empty)));
        if (!string.IsNullOrEmpty(block.CustomName))
            SelectedProperties.Add(new PropertyRow("Name", block.CustomName));
        SelectedProperties.Add(new PropertyRow("Type", block.TypeId));
        SelectedProperties.Add(new PropertyRow("Subtype", block.SubtypeName));
        SelectedProperties.Add(new PropertyRow("Grid", block.Grid.DisplayName));
        SelectedProperties.Add(new PropertyRow("Position (Min)", block.Min.ToString()));
        SelectedProperties.Add(new PropertyRow("Size", BlockGeometry.RotatedSize(def.Size, block.Orientation).ToString()));
        SelectedProperties.Add(new PropertyRow("Facing", $"Forward: {block.Orientation.Forward}, Up: {block.Orientation.Up}"));
        SelectedProperties.Add(new PropertyRow("Color", $"H {hsv.X * 360:0}°  S {hsv.Y * 100:0}  V {hsv.Z * 100:0}", PaintViewModel.MaskToBrush(block.ColorMaskHsv)));
        if (!string.IsNullOrEmpty(block.SkinSubtypeId))
            SelectedProperties.Add(new PropertyRow("Skin", block.SkinSubtypeId));
    }

    // ───────────────────────────── Tree helpers ─────────────────────────────

    private void RebuildGridNode(CubeGrid grid)
    {
        var index = -1;
        for (var i = 0; i < TreeRoots.Count; i++)
        {
            if (TreeRoots[i] is GridNodeViewModel g && g.Grid == grid)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            return;

        var old = TreeRoots[index];
        foreach (var node in old.Children.SelectMany(c => c.Children).OfType<BlockNodeViewModel>())
            _nodeByBlock.Remove(node.Block);

        var replacement = new GridNodeViewModel(grid, Definitions) { IsExpanded = old.IsExpanded };
        // Keep the same block-type groups open as before.
        var openGroups = old.Children.OfType<BlockGroupNodeViewModel>().Where(c => c.IsExpanded).Select(c => c.Name).ToHashSet();
        foreach (var group in replacement.Children.OfType<BlockGroupNodeViewModel>())
            group.IsExpanded = openGroups.Contains(group.Name);

        _syncingSelection = true;
        try
        {
            TreeRoots[index] = replacement;
        }
        finally
        {
            _syncingSelection = false;
        }
        IndexBlockNodes(replacement);
    }

    private void IndexBlockNodes(TreeNodeViewModel root)
    {
        foreach (var node in root.Children.SelectMany(c => c.Children).OfType<BlockNodeViewModel>())
            _nodeByBlock[node.Block] = node;
    }

    // ───────────────────────────── Misc ─────────────────────────────

    private static string SuggestCopyName(string parent, string name)
    {
        for (var i = 2; ; i++)
        {
            var candidate = $"{name} ({i})";
            if (!Directory.Exists(Path.Combine(parent, candidate)))
                return candidate;
        }
    }

    private void ShowError(string title, Exception ex)
    {
        StatusText = $"{title}: {ex.Message}";
        MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

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
