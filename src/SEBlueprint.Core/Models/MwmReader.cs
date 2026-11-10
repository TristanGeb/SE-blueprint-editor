using System.Numerics;
using System.Text;

namespace SEBlueprint.Core.Models;

/// <summary>
/// Reads Space Engineers .mwm model files.
///
/// Layout (versions 01066002 and newer):
///   string "Debug", int n, n × string        — debug strings, one is "Version:01157002"
///   int n, n × (string tag, int offset)     — index of sections and where they start in the file
///   sections: string tag, then the data     — e.g. "Vertices", "MeshParts", "BoundingBox", "LODs"
///
/// Strings are .NET BinaryReader strings (7-bit length prefix + UTF-8).
/// Only what the editor needs is read: positions, triangle lists with material info, bounds and LOD links.
/// </summary>
public static class MwmReader
{
    /// <summary>First version that has the section index (older files are rare and not supported).</summary>
    public const int MinimumVersion = 01066002;

    public static MwmModel Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static MwmModel Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (reader.ReadString() != "Debug")
            throw new InvalidDataException("Not an .mwm model (missing Debug header).");
        var version = 0;
        var debugCount = reader.ReadInt32();
        for (var i = 0; i < debugCount; i++)
        {
            var s = reader.ReadString();
            if (s.StartsWith("Version:", StringComparison.Ordinal))
                int.TryParse(s.AsSpan(8), out version);
        }
        if (version < MinimumVersion)
            throw new NotSupportedException($"Unsupported .mwm version {version}.");

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var sectionCount = reader.ReadInt32();
        for (var i = 0; i < sectionCount; i++)
        {
            var tag = reader.ReadString();
            index[tag] = reader.ReadInt32();
        }

        bool Seek(string tag)
        {
            if (!index.TryGetValue(tag, out var offset))
                return false;
            stream.Position = offset;
            var actual = reader.ReadString();
            if (actual != tag)
                throw new InvalidDataException($"Section '{tag}' not found at its indexed offset (found '{actual}').");
            return true;
        }

        var positions = Seek("Vertices") ? ReadPositions(reader) : Array.Empty<Vector3>();
        var texCoords = Seek("TexCoords0") ? ReadTexCoords(reader) : Array.Empty<Vector2>();
        if (texCoords.Length != positions.Length)
            texCoords = Array.Empty<Vector2>(); // mismatched data is useless; draw untextured instead
        var parts = new List<MwmMeshPart>();
        if (Seek("MeshParts"))
        {
            parts = ReadMeshParts(reader, version);
            // Self-check: the material data has version-specific fields, so make sure we ended exactly
            // where the next section begins. If not, the triangle data read so far is still usable.
            var end = stream.Position;
            var next = index.Values.Where(o => o >= index["MeshParts"] && o != index["MeshParts"]).DefaultIfEmpty(-1).Min();
            if (next >= 0 && end != next)
                throw new InvalidDataException($"MeshParts ended at {end} but the next section starts at {next}.");
        }
        var swap = Seek("SwapWindingOrder") && reader.ReadBoolean();

        Vector3 min = default, max = default;
        if (Seek("BoundingBox"))
        {
            min = ReadVector3(reader);
            max = ReadVector3(reader);
        }

        string? asset = null;
        if (Seek("GeometryDataAsset"))
        {
            asset = reader.ReadString();
            if (asset.Length == 0)
                asset = null;
        }

        var dummies = new List<MwmDummy>();
        if (Seek("Dummies"))
        {
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                var m = new float[16];
                for (var k = 0; k < 16; k++)
                    m[k] = reader.ReadSingle();
                var properties = new Dictionary<string, string>(StringComparer.Ordinal);
                var propertyCount = reader.ReadInt32();
                for (var k = 0; k < propertyCount; k++)
                    properties[reader.ReadString()] = reader.ReadString();
                dummies.Add(new MwmDummy(name, new Matrix4x4(
                    m[0], m[1], m[2], m[3],
                    m[4], m[5], m[6], m[7],
                    m[8], m[9], m[10], m[11],
                    m[12], m[13], m[14], m[15]), properties));
            }
        }

        var lods = new List<MwmLod>();
        if (Seek("LODs"))
        {
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var distance = reader.ReadSingle();
                var model = reader.ReadString();
                reader.ReadString(); // render quality (usually empty)
                lods.Add(new MwmLod(distance, model));
            }
        }

        return new MwmModel
        {
            Version = version,
            Positions = positions,
            TexCoords = texCoords,
            Parts = parts,
            SwapWindingOrder = swap,
            BoundsMin = min,
            BoundsMax = max,
            GeometryAsset = asset,
            Lods = lods,
            Dummies = dummies,
        };
    }

    /// <summary>Positions are packed as 4 half-floats: x, y, z and a scale w (position = xyz · w).</summary>
    private static Vector3[] ReadPositions(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            var x = (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
            var y = (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
            var z = (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
            var w = (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
            result[i] = new Vector3(x, y, z) * w;
        }
        return result;
    }

    /// <summary>Texture coordinates are packed as 2 half-floats (u, v), DirectX convention (v grows downward).</summary>
    private static Vector2[] ReadTexCoords(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var result = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var u = (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
            var v = (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
            result[i] = new Vector2(u, v);
        }
        return result;
    }

    private static List<MwmMeshPart> ReadMeshParts(BinaryReader reader, int version)
    {
        var count = reader.ReadInt32();
        var parts = new List<MwmMeshPart>(count);
        for (var i = 0; i < count; i++)
        {
            reader.ReadInt32(); // material hash
            var indexCount = reader.ReadInt32();
            var indices = new int[indexCount];
            for (var j = 0; j < indexCount; j++)
                indices[j] = reader.ReadInt32();

            string? materialName = null, technique = null;
            var textures = new Dictionary<string, string>(StringComparer.Ordinal);
            if (reader.ReadBoolean())
                ReadMaterial(reader, version, textures, out materialName, out technique);

            parts.Add(new MwmMeshPart
            {
                Indices = indices,
                MaterialName = materialName,
                Technique = technique,
                Textures = textures,
            });
        }
        return parts;
    }

    private static void ReadMaterial(BinaryReader reader, int version, Dictionary<string, string> textures,
        out string? materialName, out string? technique)
    {
        materialName = reader.ReadString();

        var textureCount = reader.ReadInt32();
        for (var i = 0; i < textureCount; i++)
        {
            var key = reader.ReadString();
            textures[key] = reader.ReadString();
        }

        var userDataCount = reader.ReadInt32();
        for (var i = 0; i < userDataCount; i++)
        {
            reader.ReadString();
            reader.ReadString();
        }

        technique = reader.ReadString();
        if (technique == "GLASS")
        {
            reader.ReadString(); // glass material, clockwise faces
            reader.ReadString(); // glass material, counter-clockwise faces
            reader.ReadBoolean(); // smooth normals
        }
    }

    private static Vector3 ReadVector3(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
