using System.Collections.Concurrent;
using System.IO;
using BCnEncoder.Decoder;
using BCnEncoder.Shared.ImageFiles;
using HelixToolkit.SharpDX;

namespace SEBlueprint.Rendering;

/// <summary>
/// Loads the game's .dds textures from its Content folder, once per session.
///
/// The game's color textures (_cm.dds) keep metalness in their alpha channel. Helix uses texture alpha as
/// transparency and has no switch to ignore it, so blocks came out see-through. Each texture is therefore
/// decoded (BC7 etc. → RGBA), made fully opaque, and handed to Helix as an uncompressed DDS with its mip levels.
///
/// File reading and decoding happen on a background thread (<see cref="Preload"/>); creating the GPU texture
/// happens on the UI thread (<see cref="Get"/>).
/// </summary>
/// <summary>
/// How large textures may be. The value is the maximum width/height in pixels; larger game textures are loaded
/// at a reduced size (using their smaller built-in mip levels). Each step down quarters texture memory.
/// </summary>
public enum TextureQuality
{
    Low = 256,
    Medium = 512,
    High = 1024,
    Full = 2048,
}

public static class TextureCache
{
    /// <summary>Largest width/height kept. Uncompressed textures are 4× bigger than the game's files, so this bounds memory.</summary>
    public static int MaxSize { get; private set; } = (int)TextureQuality.High;

    /// <summary>
    /// Changes the texture size limit. Already-loaded textures were made for the old limit, so they are dropped
    /// and reloaded on demand. Call on the UI thread, before rebuilding the scene.
    /// </summary>
    public static void SetQuality(TextureQuality quality)
    {
        var size = (int)quality;
        if (size == MaxSize)
            return;
        MaxSize = size;
        Prepared.Clear();
        Models.Clear();
        GC.Collect(); // release the old textures' memory now rather than whenever the GC gets round to it
    }

    private static readonly ConcurrentDictionary<string, byte[]?> Prepared = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, TextureModel?> Models = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many textures are loaded, how many failed, and how many bytes the loaded ones take.</summary>
    public static (int Loaded, int Failed, long Bytes) Stats =>
        (Prepared.Values.Count(b => b is not null), Prepared.Values.Count(b => b is null), Prepared.Values.Sum(b => (long)(b?.Length ?? 0)));

    /// <summary>Reads and prepares the given textures (skipping ones already done). Safe to call off the UI thread.</summary>
    public static void Preload(string contentFolder, IEnumerable<string> relativePaths)
    {
        var before = Prepared.Count;
        Parallel.ForEach(relativePaths.Distinct(StringComparer.OrdinalIgnoreCase), path =>
            Prepared.GetOrAdd(FullPath(contentFolder, path), Prepare));

        // Decoding leaves large temporary buffers behind (on .NET's large-object heap, which isn't compacted
        // by default). Compact once after a batch of new textures so that memory goes back to Windows.
        if (Prepared.Count != before)
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
        }
    }

    /// <summary>The GPU texture for a path, or null if the file is missing or unreadable. UI thread only.</summary>
    public static TextureModel? Get(string contentFolder, string relativePath)
    {
        var full = FullPath(contentFolder, relativePath);
        if (!Models.TryGetValue(full, out var model))
        {
            var dds = Prepared.GetOrAdd(full, Prepare);
            model = dds is null ? null : TextureModel.Create(new MemoryStream(dds, writable: false));
            Models[full] = model;
        }
        return model;
    }

    private static string FullPath(string contentFolder, string relativePath)
    {
        var path = Path.Combine(contentFolder, relativePath.TrimStart('\\', '/'));
        return path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? path : path + ".dds";
    }

    /// <summary>Decodes a game texture into an opaque, uncompressed RGBA DDS (or null on failure).</summary>
    private static byte[]? Prepare(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
                return null;

            DdsFile file;
            using (var stream = File.OpenRead(fullPath))
                file = DdsFile.Load(stream);

            var width = (int)file.header.dwWidth;
            var height = (int)file.header.dwHeight;
            var mipData = file.Faces[0].MipMaps;
            if (mipData.Length == 0 || width <= 0 || height <= 0)
                return null;

            // Skip the largest levels until the texture fits in MaxSize. Only the kept levels are decoded:
            // decoding a 4096² original just to throw it away cost ~100 MB of temporary memory per texture.
            var first = 0;
            while (first < mipData.Length - 1 && (width >> first > MaxSize || height >> first > MaxSize))
                first++;

            var decoder = new BcDecoder();
            var format = decoder.GetFormat(file);
            var levels = new List<(int W, int H, byte[] Rgba)>();
            for (var level = first; level < mipData.Length; level++)
            {
                var w = Math.Max(1, width >> level);
                var h = Math.Max(1, height >> level);
                var pixels = decoder.DecodeRaw(mipData[level].Data, w, h, format);
                if (pixels.Length < w * h)
                    break;
                var rgba = new byte[w * h * 4];
                for (var i = 0; i < w * h; i++)
                {
                    rgba[4 * i] = pixels[i].r;
                    rgba[4 * i + 1] = pixels[i].g;
                    rgba[4 * i + 2] = pixels[i].b;
                    rgba[4 * i + 3] = 255; // alpha is metalness in the game's textures, not transparency
                }
                levels.Add((w, h, rgba));
            }
            return levels.Count == 0 ? null : WriteRgbaDds(levels);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException or EndOfStreamException or FormatException)
        {
            return null; // the part is drawn untextured
        }
    }

    /// <summary>Writes a plain uncompressed 32-bit RGBA DDS with the given mip levels (largest first).</summary>
    private static byte[] WriteRgbaDds(IReadOnlyList<(int W, int H, byte[] Rgba)> levels)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var (width, height, _) = levels[0];

        w.Write(0x20534444u);                 // "DDS "
        w.Write(124u);                        // header size
        w.Write(0x1u | 0x2u | 0x4u | 0x8u | 0x1000u | 0x20000u); // CAPS|HEIGHT|WIDTH|PITCH|PIXELFORMAT|MIPMAPCOUNT
        w.Write((uint)height);
        w.Write((uint)width);
        w.Write((uint)(width * 4));           // pitch
        w.Write(0u);                          // depth
        w.Write((uint)levels.Count);          // mip count
        for (var i = 0; i < 11; i++)
            w.Write(0u);                      // reserved
        // Pixel format: 32-bit RGBA, bytes in R, G, B, A order (= DXGI R8G8B8A8_UNORM)
        w.Write(32u);
        w.Write(0x40u | 0x1u);                // RGB | ALPHAPIXELS
        w.Write(0u);                          // no FourCC
        w.Write(32u);
        w.Write(0x000000FFu);
        w.Write(0x0000FF00u);
        w.Write(0x00FF0000u);
        w.Write(0xFF000000u);
        w.Write(0x1000u | 0x400000u | 0x8u);  // TEXTURE | MIPMAP | COMPLEX
        w.Write(0u);
        w.Write(0u);
        w.Write(0u);
        w.Write(0u);

        foreach (var level in levels)
            w.Write(level.Rgba);
        return ms.ToArray();
    }
}
