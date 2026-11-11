using SEBlueprint.Core.Model;

namespace SEBlueprint.Core.Editing;

public enum GridLinkKind
{
    /// <summary>Rotor, hinge, piston or suspension base and the head/top/wheel on the other grid.</summary>
    Mechanical,

    /// <summary>Two connectors locked together.</summary>
    Connector,
}

/// <summary>
/// A link between two blocks on different grids that a delete would break: one end is being deleted,
/// the other stays in the blueprint and keeps pointing at the deleted block's EntityId.
/// </summary>
public sealed record BrokenGridLink(Block Deleted, Block Remaining, GridLinkKind Kind);

/// <summary>
/// Finds rotor/hinge/piston/suspension and connector links between grids.
/// Bases store their head in &lt;TopBlockId&gt;, heads store their base in &lt;ParentEntityId&gt;,
/// and docked connectors store each other in &lt;ConnectedEntityId&gt;.
/// </summary>
public static class GridLinks
{
    private static readonly (string Field, GridLinkKind Kind)[] LinkFields =
    {
        ("TopBlockId", GridLinkKind.Mechanical),
        ("ParentEntityId", GridLinkKind.Mechanical),
        ("ConnectedEntityId", GridLinkKind.Connector),
    };

    /// <summary>
    /// Links that deleting <paramref name="deleting"/> would leave half-attached. A link with both
    /// ends deleted is not reported. Each pair of blocks is reported once, even when both ends point
    /// at each other.
    /// </summary>
    public static IReadOnlyList<BrokenGridLink> FindBrokenBy(Blueprint blueprint, IEnumerable<Block> deleting)
    {
        var deleted = deleting.ToHashSet();
        if (deleted.Count == 0)
            return Array.Empty<BrokenGridLink>();

        var byId = new Dictionary<string, Block>();
        foreach (var block in blueprint.AllBlocks)
        {
            var id = block.EntityId;
            if (id is not null)
                byId.TryAdd(id, block);
        }

        var seen = new HashSet<(Block, Block)>();
        var result = new List<BrokenGridLink>();
        foreach (var block in blueprint.AllBlocks)
        {
            foreach (var (field, kind) in LinkFields)
            {
                var targetId = block.Element.Element(field)?.Value.Trim();
                if (string.IsNullOrEmpty(targetId) || targetId == "0" || !byId.TryGetValue(targetId, out var target))
                    continue;
                if (target.Grid == block.Grid)
                    continue; // not a subgrid link

                var blockDeleted = deleted.Contains(block);
                var targetDeleted = deleted.Contains(target);
                if (blockDeleted == targetDeleted)
                    continue; // both kept, or both deleted together

                var (gone, kept) = blockDeleted ? (block, target) : (target, block);
                if (seen.Add((gone, kept)))
                    result.Add(new BrokenGridLink(gone, kept, kind));
            }
        }
        return result;
    }
}
