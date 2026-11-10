using SEBlueprint.Core.Model;

namespace SEBlueprint.Core.Editing;

/// <summary>
/// One undoable change to a blueprint. All edits go through commands run by <see cref="EditorSession"/>;
/// nothing should change the model directly, or undo/redo and the UI would get out of sync.
/// </summary>
public interface IEditCommand
{
    /// <summary>Short description for the Edit menu, e.g. "Paint 12 blocks".</summary>
    string Name { get; }

    /// <summary>Applies the change and reports what it touched.</summary>
    EditChange Do();

    /// <summary>Reverts exactly what <see cref="Do"/> changed.</summary>
    EditChange Undo();
}

/// <summary>What an edit touched, so the 3D view and tree can update only what's needed.</summary>
/// <param name="Grids">Grids whose blocks changed.</param>
/// <param name="StructureChanged">True if blocks were added or removed (the tree needs rebuilding), false for property changes like paint.</param>
public sealed record EditChange(IReadOnlyCollection<CubeGrid> Grids, bool StructureChanged);
