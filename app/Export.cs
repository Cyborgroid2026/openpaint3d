using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace OpenPaint3D;

/// <summary>Image, 3D model and project file writers.</summary>
public static class Export
{
    public const string ProjectExtension = ".op3d";

    // -------------------------------------------------------------- projects

    public static void SaveProject(Engine e, string path)
    {
        var blob = e.SaveProject();
        using var fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        gz.Write(blob);
    }

    public static bool LoadProject(Engine e, string path)
    {
        using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        return e.LoadProject(ms.ToArray());
    }

    // ---------------------------------------------------------------- images

    /// <summary>The picture centred on one A4 page (portrait or landscape) with a 10 mm margin.</summary>
    public static void SavePdf(Engine e, string path, bool landscape)
    {
        float pw = landscape ? 842 : 595, ph = landscape ? 595 : 842, margin = 28;
        using var img = e.Snapshot(forceOpaque: true);
        using var stream = File.Create(path);
        using var doc = SKDocument.CreatePdf(stream);
        var canvas = doc.BeginPage(pw, ph);
        float k = Math.Min((pw - 2 * margin) / img.Width, (ph - 2 * margin) / img.Height);
        float w = img.Width * k, h = img.Height * k;
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        canvas.DrawImage(img, SKRect.Create((pw - w) / 2, (ph - h) / 2, w, h), paint);
        doc.EndPage();
        doc.Close();
    }

    public static void SaveImage(Engine e, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        bool keepAlpha = ext is ".png" or ".webp" or ".tif" or ".tiff" or ".gif";
        using var img = e.Snapshot(forceOpaque: !keepAlpha);
        switch (ext)
        {
            case ".bmp":
                WriteBmp(img, path);
                return;
            case ".tif" or ".tiff":
                WriteTiff(img, path);
                return;
            case ".gif":
                WriteGif(img, path);
                return;
        }
        var fmt = ext switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        };
        using var data = img.Encode(fmt, 92);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    /// <summary>Straight RGBA pixels of an image.</summary>
    private static byte[] Rgba(SKImage img)
    {
        var info = new SKImageInfo(img.Width, img.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var buf = new byte[img.Width * img.Height * 4];
        unsafe
        {
            fixed (byte* p = buf) img.ReadPixels(info, (IntPtr)p, img.Width * 4, 0, 0);
        }
        return buf;
    }

    private static void WriteBmp(SKImage img, string path)
    {
        int w = img.Width, h = img.Height, row = (w * 3 + 3) & ~3;
        var px = Rgba(img);
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(54 + row * h);
        bw.Write(0);
        bw.Write(54);
        bw.Write(40);
        bw.Write(w);
        bw.Write(h);
        bw.Write((short)1);
        bw.Write((short)24);
        bw.Write(0);
        bw.Write(row * h);
        bw.Write(2835);
        bw.Write(2835);
        bw.Write(0);
        bw.Write(0);
        var line = new byte[row];
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                line[x * 3] = px[i + 2];
                line[x * 3 + 1] = px[i + 1];
                line[x * 3 + 2] = px[i];
            }
            bw.Write(line);
        }
    }

    private static void WriteTiff(SKImage img, string path)
    {
        // baseline little-endian TIFF, uncompressed RGBA in one strip
        int w = img.Width, h = img.Height;
        var px = Rgba(img);
        using var bw = new BinaryWriter(File.Create(path));
        const int entries = 11;
        int ifd = 8, ifdSize = 2 + entries * 12 + 4, bpsOffset = ifd + ifdSize, dataOffset = bpsOffset + 8;
        bw.Write((byte)'I');
        bw.Write((byte)'I');
        bw.Write((short)42);
        bw.Write(ifd);
        bw.Write((short)entries);
        void Tag(short tag, short type, int count, int value)
        {
            bw.Write(tag);
            bw.Write(type);
            bw.Write(count);
            if (type == 3 && count == 1) { bw.Write((short)value); bw.Write((short)0); }
            else bw.Write(value);
        }
        Tag(256, 4, 1, w);                 // ImageWidth
        Tag(257, 4, 1, h);                 // ImageLength
        Tag(258, 3, 4, bpsOffset);         // BitsPerSample -> 8,8,8,8
        Tag(259, 3, 1, 1);                 // Compression: none
        Tag(262, 3, 1, 2);                 // Photometric: RGB
        Tag(273, 4, 1, dataOffset);        // StripOffsets
        Tag(277, 3, 1, 4);                 // SamplesPerPixel
        Tag(278, 4, 1, h);                 // RowsPerStrip
        Tag(279, 4, 1, px.Length);         // StripByteCounts
        Tag(284, 3, 1, 1);                 // PlanarConfiguration: chunky
        Tag(338, 3, 1, 2);                 // ExtraSamples: unassociated alpha
        bw.Write(0);
        for (int i = 0; i < 4; i++) bw.Write((short)8);
        bw.Write(px);
    }

    private static void WriteGif(SKImage img, string path)
    {
        // 6x7x6 colour cube + transparent index, ordered dithering, LZW
        int w = Math.Min(img.Width, 65535), h = Math.Min(img.Height, 65535);
        var px = Rgba(img);
        var palette = new byte[256 * 3];
        for (int r = 0, k = 0; r < 6; r++)
            for (int g = 0; g < 7; g++)
                for (int b = 0; b < 6; b++, k++)
                {
                    palette[k * 3] = (byte)(r * 51);
                    palette[k * 3 + 1] = (byte)Math.Round(g * 42.5);
                    palette[k * 3 + 2] = (byte)(b * 51);
                }
        const int transparent = 255;
        int[] bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        var idx = new byte[w * h];
        bool anyTransparent = false;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * img.Width + x) * 4;
                if (px[i + 3] < 128)
                {
                    idx[y * w + x] = transparent;
                    anyTransparent = true;
                    continue;
                }
                double t = (bayer[(y & 3) * 4 + (x & 3)] + 0.5) / 16 - 0.5;
                int r = Math.Clamp((int)Math.Round(px[i] / 51.0 + t), 0, 5);
                int g = Math.Clamp((int)Math.Round(px[i + 1] / 42.5 + t), 0, 6);
                int b = Math.Clamp((int)Math.Round(px[i + 2] / 51.0 + t), 0, 5);
                idx[y * w + x] = (byte)(r * 42 + g * 6 + b);
            }
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write(Encoding.ASCII.GetBytes("GIF89a"));
        bw.Write((short)w);
        bw.Write((short)h);
        bw.Write((byte)0xF7);  // global colour table, 256 entries
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write(palette);
        if (anyTransparent)
        {
            bw.Write(new byte[] { 0x21, 0xF9, 4, 1, 0, 0, transparent, 0 });
        }
        bw.Write((byte)0x2C);
        bw.Write((short)0);
        bw.Write((short)0);
        bw.Write((short)w);
        bw.Write((short)h);
        bw.Write((byte)0);
        bw.Write((byte)8);
        var lzw = Lzw(idx, 8);
        for (int i = 0; i < lzw.Length; i += 255)
        {
            int n = Math.Min(255, lzw.Length - i);
            bw.Write((byte)n);
            bw.Write(lzw, i, n);
        }
        bw.Write((byte)0);
        bw.Write((byte)0x3B);
    }

    private static byte[] Lzw(byte[] data, int minCode)
    {
        int clear = 1 << minCode, end = clear + 1;
        var output = new List<byte>();
        int bitBuf = 0, bitCount = 0, codeSize = minCode + 1, next = end + 1;
        var dict = new Dictionary<long, int>();
        void Emit(int code)
        {
            bitBuf |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                output.Add((byte)(bitBuf & 0xFF));
                bitBuf >>= 8;
                bitCount -= 8;
            }
        }
        Emit(clear);
        int prefix = -1;
        foreach (var b in data)
        {
            if (prefix < 0)
            {
                prefix = b;
                continue;
            }
            long key = ((long)prefix << 8) | b;
            if (dict.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }
            Emit(prefix);
            if (next < 4096)
            {
                dict[key] = next++;
                if (next > (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                Emit(clear);
                dict.Clear();
                codeSize = minCode + 1;
                next = end + 1;
            }
            prefix = b;
        }
        if (prefix >= 0) Emit(prefix);
        Emit(end);
        if (bitCount > 0) output.Add((byte)(bitBuf & 0xFF));
        return output.ToArray();
    }

    // ---------------------------------------------------------------- 3D models

    /// <summary>Engine world (x right, y down, z into the screen) to Y-up, viewer-facing +Z.</summary>
    private static (float, float, float) YUp(float x, float y, float z) => (x, -y, -z);

    public static void SaveGlb(Engine e, string path)
    {
        var t = e.ExportTriangles();
        int n = t.Length / 9;
        if (n == 0) throw new InvalidOperationException("내보낼 3D 개체가 없습니다. 3D 셰이프나 3D 라이브러리로 개체를 추가하세요.");
        // centre the model and convert pixels to centimetres-ish (1 px = 1 mm)
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < n; i++) { cx += t[i * 9]; cy += t[i * 9 + 1]; cz += t[i * 9 + 2]; }
        cx /= n; cy /= n; cz /= n;
        const float scale = 0.001f;
        var pos = new float[n * 3];
        var nrm = new float[n * 3];
        var col = new float[n * 3];
        float[] min = { float.MaxValue, float.MaxValue, float.MaxValue }, max = { float.MinValue, float.MinValue, float.MinValue };
        for (int i = 0; i < n; i++)
        {
            var (x, y, z) = YUp((float)(t[i * 9] - cx) * scale, (float)(t[i * 9 + 1] - cy) * scale, (float)(t[i * 9 + 2] - cz) * scale);
            var (nx, ny, nz) = YUp(t[i * 9 + 3], t[i * 9 + 4], t[i * 9 + 5]);
            pos[i * 3] = x; pos[i * 3 + 1] = y; pos[i * 3 + 2] = z;
            nrm[i * 3] = nx; nrm[i * 3 + 1] = ny; nrm[i * 3 + 2] = nz;
            for (int k = 0; k < 3; k++)
            {
                col[i * 3 + k] = t[i * 9 + 6 + k];
                min[k] = Math.Min(min[k], pos[i * 3 + k]);
                max[k] = Math.Max(max[k], pos[i * 3 + k]);
            }
        }
        int bytes = n * 12;
        var bin = new byte[bytes * 3];
        Buffer.BlockCopy(pos, 0, bin, 0, bytes);
        Buffer.BlockCopy(nrm, 0, bin, bytes, bytes);
        Buffer.BlockCopy(col, 0, bin, bytes * 2, bytes);
        string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var json = $$"""
        {"asset":{"version":"2.0","generator":"OpenPaint 3D"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0,"name":"OpenPaint3D"}],
        "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"COLOR_0":2},"material":0,"mode":4}]}],
        "materials":[{"pbrMetallicRoughness":{"baseColorFactor":[1,1,1,1],"metallicFactor":0,"roughnessFactor":0.6},"doubleSided":true}],
        "buffers":[{"byteLength":{{bin.Length}}}],
        "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":{{bytes}},"target":34962},{"buffer":0,"byteOffset":{{bytes}},"byteLength":{{bytes}},"target":34962},{"buffer":0,"byteOffset":{{bytes * 2}},"byteLength":{{bytes}},"target":34962}],
        "accessors":[{"bufferView":0,"componentType":5126,"count":{{n}},"type":"VEC3","min":[{{F(min[0])}},{{F(min[1])}},{{F(min[2])}}],"max":[{{F(max[0])}},{{F(max[1])}},{{F(max[2])}}]},
        {"bufferView":1,"componentType":5126,"count":{{n}},"type":"VEC3"},{"bufferView":2,"componentType":5126,"count":{{n}},"type":"VEC3"}]}
        """;
        var jsonBytes = Encoding.UTF8.GetBytes(json.Replace("\n", "").Replace("\r", ""));
        int jsonPad = (4 - jsonBytes.Length % 4) % 4, binPad = (4 - bin.Length % 4) % 4;
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write(0x46546C67u);             // "glTF"
        bw.Write(2u);
        bw.Write((uint)(12 + 8 + jsonBytes.Length + jsonPad + 8 + bin.Length + binPad));
        bw.Write((uint)(jsonBytes.Length + jsonPad));
        bw.Write(0x4E4F534Au);             // "JSON"
        bw.Write(jsonBytes);
        for (int i = 0; i < jsonPad; i++) bw.Write((byte)' ');
        bw.Write((uint)(bin.Length + binPad));
        bw.Write(0x004E4942u);             // "BIN\0"
        bw.Write(bin);
        for (int i = 0; i < binPad; i++) bw.Write((byte)0);
    }

    public static void Save3mf(Engine e, string path)
    {
        var t = e.ExportTriangles();
        int n = t.Length / 9;
        if (n == 0) throw new InvalidOperationException("내보낼 3D 개체가 없습니다. 3D 셰이프나 3D 라이브러리로 개체를 추가하세요.");
        // 3MF is Z-up in millimetres: engine (x, y, z) -> (x, z, -y), 1 px = 0.1 mm
        var verts = new Dictionary<(long, long, long), int>();
        var vlist = new StringBuilder();
        var colors = new Dictionary<string, int>();
        var tris = new StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        int Vertex(int i)
        {
            double x = t[i * 9] * 0.1, y = t[i * 9 + 2] * 0.1, z = -t[i * 9 + 1] * 0.1;
            var key = ((long)Math.Round(x * 1000), (long)Math.Round(y * 1000), (long)Math.Round(z * 1000));
            if (!verts.TryGetValue(key, out int id))
            {
                id = verts.Count;
                verts[key] = id;
                vlist.Append(inv, $"<vertex x=\"{x:0.###}\" y=\"{y:0.###}\" z=\"{z:0.###}\"/>");
            }
            return id;
        }
        double zmin = double.MaxValue;
        for (int i = 0; i < n; i++) zmin = Math.Min(zmin, -t[i * 9 + 1] * 0.1);
        for (int f = 0; f < n / 3; f++)
        {
            int a = Vertex(f * 3), b = Vertex(f * 3 + 1), c = Vertex(f * 3 + 2);
            if (a == b || b == c || a == c) continue;
            int k = f * 3 * 9;
            var hex = $"#{(int)(t[k + 6] * 255):X2}{(int)(t[k + 7] * 255):X2}{(int)(t[k + 8] * 255):X2}FF";
            if (!colors.TryGetValue(hex, out int ci)) colors[hex] = ci = colors.Count;
            // the axis swap has determinant +1, so the engine's outward CCW winding is kept
            tris.Append($"<triangle v1=\"{a}\" v2=\"{b}\" v3=\"{c}\" pid=\"1\" p1=\"{ci}\"/>");
        }
        var mats = string.Concat(colors.OrderBy(kv => kv.Value).Select(kv => $"<base name=\"c{kv.Value}\" displaycolor=\"{kv.Key}\"/>"));
        var model = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <model unit="millimeter" xml:lang="ko-KR" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">
            <resources><basematerials id="1">{mats}</basematerials>
            <object id="2" type="model" pid="1" pindex="0"><mesh><vertices>{vlist}</vertices><triangles>{tris}</triangles></mesh></object></resources>
            <build><item objectid="2" transform="1 0 0 0 1 0 0 0 1 0 0 {(-zmin).ToString("0.###", inv)}"/></build>
            </model>
            """;
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            using var s = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            s.Write(content);
        }
        Add("[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/></Types>""");
        Add("_rels/.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Target="/3D/3dmodel.model" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/></Relationships>""");
        Add("3D/3dmodel.model", model);
    }
}
