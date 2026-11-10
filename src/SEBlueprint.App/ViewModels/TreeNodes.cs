using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Model;

namespace SEBlueprint.App.ViewModels;

/// <summary>One row in the blueprint tree (grid, block group, or single block).</summary>
public abstract partial class TreeNodeViewModel : ObservableObject
{
    protected TreeNodeViewModel(TreeNodeViewModel? parent) => Parent = parent;

    public TreeNodeViewModel? Parent { get; }

    public abstract string Header { get; }

    /// <summary>Every block under this node. Selecting the node selects all of these.</summary>
    public virtual IEnumerable<Block> Blocks => Children.SelectMany(c => c.Blocks);

    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Expands every parent so this node becomes visible in the tree.</summary>
    public void ExpandParents()
    {
        for (var p = Parent; p is not null; p = p.Parent)
            p.IsExpanded = true;
    }
}

/// <summary>A grid. Its children are its blocks, grouped by block type.</summary>
public sealed class GridNodeViewModel : TreeNodeViewModel
{
    public GridNodeViewModel(CubeGrid grid, DefinitionRepository definitions)
        : base(null)
    {
        Grid = grid;
        var groups = grid.Blocks
            .GroupBy(b => definitions.Get(b).DisplayName)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in groups)
        {
            var groupNode = new BlockGroupNodeViewModel(this, group.Key, group.Count());
            foreach (var block in group)
                groupNode.Children.Add(new BlockNodeViewModel(groupNode, block, group.Key));
            Children.Add(groupNode);
        }
    }

    public CubeGrid Grid { get; }

    public override IEnumerable<Block> Blocks => Grid.Blocks;

    public override string Header => $"{Grid.DisplayName}  ({Grid.GridSize} grid, {Grid.Blocks.Count:N0} blocks)";
}

public sealed class BlockGroupNodeViewModel : TreeNodeViewModel
{
    private readonly int _count;

    public BlockGroupNodeViewModel(TreeNodeViewModel parent, string name, int count)
        : base(parent)
    {
        Name = name;
        _count = count;
    }

    /// <summary>The block type's display name, e.g. "Light Armor Block".</summary>
    public string Name { get; }

    public override string Header => $"{Name}  ×{_count:N0}";
}

public sealed class BlockNodeViewModel : TreeNodeViewModel
{
    public BlockNodeViewModel(TreeNodeViewModel parent, Block block, string displayName)
        : base(parent)
    {
        Block = block;
        Header = string.IsNullOrEmpty(block.CustomName)
            ? $"{displayName}  {block.Min}"
            : $"{block.CustomName}  {block.Min}";
    }

    public Block Block { get; }

    public override string Header { get; }

    public override IEnumerable<Block> Blocks => new[] { Block };
}
