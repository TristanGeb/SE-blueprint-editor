using System.Numerics;
using System.Xml.Linq;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Core.Editing;

/// <summary>Paints blocks one color. Undo restores each block's original color XML exactly (including "no color set").</summary>
public sealed class PaintBlocksCommand : IEditCommand
{
    private readonly Block[] _blocks;
    private readonly Vector3 _colorMask;

    /// <summary>Copy of each block's original &lt;ColorMaskHSV&gt;, or null if it had none.</summary>
    private readonly XElement?[] _originals;

    public PaintBlocksCommand(IEnumerable<Block> blocks, Vector3 colorMaskHsv)
    {
        _blocks = blocks.Distinct().ToArray();
        _colorMask = colorMaskHsv;
        _originals = _blocks
            .Select(b => b.Element.Element("ColorMaskHSV") is { } e ? new XElement(e) : null)
            .ToArray();
    }

    public string Name => _blocks.Length == 1 ? "Paint block" : $"Paint {_blocks.Length:N0} blocks";

    public EditChange Do()
    {
        foreach (var block in _blocks)
            block.ColorMaskHsv = _colorMask;
        return Change();
    }

    public EditChange Undo()
    {
        for (var i = 0; i < _blocks.Length; i++)
        {
            // Do() always leaves a ColorMaskHSV element, so it exists here.
            var current = _blocks[i].Element.Element("ColorMaskHSV");
            if (_originals[i] is { } original)
                current?.ReplaceWith(new XElement(original));
            else
                current?.Remove();
        }
        return Change();
    }

    private EditChange Change() => new(_blocks.Select(b => b.Grid).Distinct().ToArray(), StructureChanged: false);
}
