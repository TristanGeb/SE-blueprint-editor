using System.Xml.Linq;

namespace SEBlueprint.Core.Definitions;

/// <summary>
/// Turns the game's text keys (e.g. "DisplayName_Block_SmallThrust") into English names ("Ion Thruster"),
/// using Content\Data\Localization\MyTexts.resx.
/// </summary>
public sealed class Localization
{
    private readonly Dictionary<string, string> _texts;

    public Localization(Dictionary<string, string> texts) => _texts = texts;

    public static Localization Empty { get; } = new(new Dictionary<string, string>());

    public int Count => _texts.Count;

    public static Localization LoadResx(string resxPath)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var doc = XDocument.Load(resxPath);
        foreach (var data in doc.Root!.Elements("data"))
        {
            var name = data.Attribute("name")?.Value;
            var value = data.Element("value")?.Value;
            if (name is not null && value is not null)
                texts[name] = value;
        }
        return new Localization(texts);
    }

    /// <summary>Returns the translated text, or the key itself if it isn't a known key (some mods use plain names).</summary>
    public string Translate(string key) => _texts.TryGetValue(key, out var v) ? v : key;
}
