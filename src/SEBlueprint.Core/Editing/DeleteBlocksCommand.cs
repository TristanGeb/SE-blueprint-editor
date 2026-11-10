using SEBlueprint.Core.Model;

namespace SEBlueprint.Core.Editing;

/// <summary>
/// Removes blocks from their grids. Undo puts every block back at its original position in both
/// the grid's block list and the XML, so an undone delete saves exactly like the original.
/// </summary>
public sealed class DeleteBlocksCommand : IEditCommand
{
    /// <summary>Per grid, the removed blocks with their original indexes, sorted ascending.</summary>
    private readonly (CubeGrid Grid, (Block Block, int Index)[] Removals)[] _perGrid;

    public DeleteBlocksCommand(IEnumerable<Block> blocks)
    {
        _perGrid = blocks
            .Distinct()
            .GroupBy(b => b.Grid)
            .Select(g =>
            {
                // One pass over the grid to find indexes (IndexOf per block would be slow on big ships).
                var wanted = g.ToHashSet();
                var removals = new List<(Block, int)>(wanted.Count);
                for (var i = 0; i < g.Key.Blocks.Count; i++)
                {
                    if (wanted.Contains(g.Key.Blocks[i]))
                        removals.Add((g.Key.Blocks[i], i));
                }
                return (g.Key, removals.ToArray());
            })
            .Where(g => g.Item2.Length > 0)
            .ToArray();

        foreach (var (grid, removals) in _perGrid)
        {
            if (removals.Length >= grid.Blocks.Count)
                throw new InvalidOperationException(
                    $"Can't delete every block of grid \"{grid.DisplayName}\": the game can't load a blueprint with an empty grid.");
        }
    }

    public int Count => _perGrid.Sum(g => g.Removals.Length);

    public string Name => Count == 1 ? "Delete block" : $"Delete {Count:N0} blocks";

    // Both directions rebuild each grid's block list in one pass and then rewrite the XML children to match.
    // Removing/inserting one at a time would be O(blocks × removed) and take seconds on large ships.

    public EditChange Do()
    {
        foreach (var (grid, removals) in _perGrid)
        {
            var removed = removals.Select(r => r.Block).ToHashSet();
            grid.Blocks.RemoveAll(removed.Contains);
            SyncXml(grid);
        }
        return Change();
    }

    public EditChange Undo()
    {
        foreach (var (grid, removals) in _perGrid)
        {
            // Merge the removed blocks back in at their original indexes (removals are sorted by index).
            var merged = new List<Block>(grid.Blocks.Count + removals.Length);
            var next = 0;
            foreach (var block in grid.Blocks)
            {
                while (next < removals.Length && removals[next].Index == merged.Count)
                    merged.Add(removals[next++].Block);
                merged.Add(block);
            }
            while (next < removals.Length)
                merged.Add(removals[next++].Block);

            grid.Blocks.Clear();
            grid.Blocks.AddRange(merged);
            SyncXml(grid);
        }
        return Change();
    }

    /// <summary>Makes the &lt;CubeBlocks&gt; children exactly the elements of grid.Blocks, in order.</summary>
    private static void SyncXml(CubeGrid grid)
    {
        var container = grid.CubeBlocksElement;
        container.RemoveNodes(); // detach first, so Add() moves the same element objects instead of cloning them
        container.Add(grid.Blocks.Select(b => b.Element));
    }

    private EditChange Change() => new(_perGrid.Select(g => g.Grid).ToArray(), StructureChanged: true);
}
