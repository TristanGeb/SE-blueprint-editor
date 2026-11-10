using SEBlueprint.Core.IO;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Core.Editing;

/// <summary>
/// An open blueprint plus its edit history. Runs commands, keeps the undo/redo stacks,
/// tracks unsaved changes, and raises <see cref="Changed"/> after every edit, undo or redo.
/// </summary>
public sealed class EditorSession
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();

    /// <summary>The command on top of the undo stack when last saved (null = saved with an empty history).</summary>
    private IEditCommand? _savedAt;

    public EditorSession(Blueprint blueprint) => Blueprint = blueprint;

    public Blueprint Blueprint { get; }

    /// <summary>Raised after any change to the blueprint (edit, undo or redo).</summary>
    public event EventHandler<EditChange>? Changed;

    /// <summary>Raised when undo/redo availability or the unsaved-changes state may have changed.</summary>
    public event EventHandler? HistoryChanged;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoName => _undo.TryPeek(out var c) ? c.Name : null;

    public string? RedoName => _redo.TryPeek(out var c) ? c.Name : null;

    /// <summary>True if the blueprint differs from what's on disk.</summary>
    public bool IsDirty => (_undo.TryPeek(out var top) ? top : null) != _savedAt;

    public void Execute(IEditCommand command)
    {
        var change = command.Do();
        _undo.Push(command);
        _redo.Clear();
        Raise(change);
    }

    public void Undo()
    {
        if (!_undo.TryPop(out var command))
            return;
        var change = command.Undo();
        _redo.Push(command);
        Raise(change);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var command))
            return;
        var change = command.Do();
        _undo.Push(command);
        Raise(change);
    }

    /// <summary>Saves to the blueprint's own file (keeps a .bak of the original, deletes the .sbcB5 cache).</summary>
    public void Save()
    {
        BlueprintIO.Save(Blueprint);
        MarkSaved();
    }

    /// <summary>
    /// Saves as a new blueprint in its own folder, e.g. %AppData%\SpaceEngineers\Blueprints\local\My Ship v2.
    /// The blueprint's name is changed to match the folder (the game expects that), and the thumbnail is copied.
    /// </summary>
    public void SaveAsNew(string blueprintsFolder, string name)
    {
        var folder = Path.Combine(blueprintsFolder, name);
        if (File.Exists(Path.Combine(folder, BlueprintIO.BlueprintFileName)))
            throw new IOException($"A blueprint called \"{name}\" already exists.");

        var oldFolder = Blueprint.FilePath is { } p ? Path.GetDirectoryName(p) : null;
        Directory.CreateDirectory(folder);
        Blueprint.Name = name;
        BlueprintIO.Save(Blueprint, Path.Combine(folder, BlueprintIO.BlueprintFileName));

        var thumb = oldFolder is null ? null : Path.Combine(oldFolder, "thumb.png");
        if (thumb is not null && File.Exists(thumb))
            File.Copy(thumb, Path.Combine(folder, "thumb.png"), overwrite: true);

        MarkSaved();
    }

    private void MarkSaved()
    {
        _savedAt = _undo.TryPeek(out var top) ? top : null;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Raise(EditChange change)
    {
        Changed?.Invoke(this, change);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
