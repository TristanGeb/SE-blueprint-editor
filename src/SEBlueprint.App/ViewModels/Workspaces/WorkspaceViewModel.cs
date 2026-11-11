using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using SEBlueprint.Core.Model;

namespace SEBlueprint.App.ViewModels.Workspaces;

/// <summary>
/// A workspace is one way of using the editor (editing, analyzing...). All workspaces share the open blueprint,
/// its undo history, the 3D view, the camera and the selection; each brings its own panel (chosen by a
/// DataTemplate for its type), decides whether editing is allowed, and may recolor the 3D view.
/// </summary>
public abstract partial class WorkspaceViewModel : ObservableObject
{
    protected WorkspaceViewModel(MainViewModel main) => Main = main;

    protected MainViewModel Main { get; }

    public abstract string Name { get; }

    /// <summary>Shown as a tooltip on the workspace tab.</summary>
    public abstract string Description { get; }

    /// <summary>Whether edit commands (paint, delete, undo...) are available. Analysis workspaces are read-only.</summary>
    public abstract bool AllowsEditing { get; }

    /// <summary>How to color each block in 3D (RGBA 0..1), or null to show the blueprint's own paint and textures.</summary>
    [ObservableProperty]
    private Func<Block, Vector4>? _colorOverride;

    /// <summary>Called when the user switches to this workspace.</summary>
    public virtual void OnActivated()
    {
    }

    /// <summary>Called when a different blueprint is opened, or the current one changed while this workspace was away.</summary>
    public virtual void OnBlueprintChanged()
    {
    }
}

/// <summary>The normal editing workspace: select, paint, delete, save.</summary>
public sealed class EditWorkspaceViewModel(MainViewModel main) : WorkspaceViewModel(main)
{
    public override string Name => "Edit";

    public override string Description => "Select, paint and delete blocks, then save the blueprint.";

    public override bool AllowsEditing => true;
}
