// Renders the SubFlow mark and wordmark into the app's Assets folder.
//   dotnet run --project tools/brand -- src/NapisyPL/Assets
//
// The mark: an ink tile with a teal "flow" stroke over two subtitle bars. At 32 px
// and below the stroke turns to noise, so small sizes draw the bars only, larger.
using SkiaSharp;

var outputDirectory = args.Length > 0 ? args[0] : Path.Combine("src", "NapisyPL", "Assets");
Directory.CreateDirectory(outputDirectory);

var inkTop = SKColor.Parse("#1D2742");
var inkBottom = SKColor.Parse("#0C1222");
var flow = SKColor.Parse("#4FD1C5");
var subtitle = SKColor.Parse("#F5C451");
var subtitleSoft = SKColor.Parse("#F5C451").WithAlpha(150);

byte[] RenderIcon(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(size / 256f);

    var tile = new SKRoundRect(new SKRect(8, 8, 248, 248), 58);
    using (var fill = new SKPaint { IsAntialias = true })
    {
        fill.Shader = SKShader.CreateLinearGradient(new SKPoint(8, 8), new SKPoint(248, 248),
            [inkTop, inkBottom], SKShaderTileMode.Clamp);
        canvas.DrawRoundRect(tile, fill);
    }

    using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3, Color = SKColors.White.WithAlpha(22) })
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(9.5f, 9.5f, 246.5f, 246.5f), 56.5f), edge);

    var small = size <= 32;
    using var bar = new SKPaint { IsAntialias = true, Color = subtitle };
    if (small)
    {
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(44, 104, 212, 144), 20), bar);
        bar.Color = subtitleSoft;
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(76, 164, 180, 204), 20), bar);
    }
    else
    {
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 20,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = flow
        };
        using var wave = new SKPath();
        wave.MoveTo(58, 102);
        wave.CubicTo(84, 62, 106, 62, 128, 102);
        wave.CubicTo(150, 142, 172, 142, 198, 102);
        canvas.DrawPath(wave, stroke);

        canvas.DrawRoundRect(new SKRoundRect(new SKRect(48, 150, 208, 178), 14), bar);
        bar.Color = subtitleSoft;
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(80, 194, 176, 222), 14), bar);
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

byte[] RenderWordmark(int height)
{
    var iconSize = height;
    using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
    using var font = new SKFont(typeface, height * 0.56f);
    var sub = "Sub";
    var flowText = "Flow";
    var subWidth = font.MeasureText(sub);
    var flowWidth = font.MeasureText(flowText);
    var gap = height * 0.24f;
    var width = (int)Math.Ceiling(iconSize + gap + subWidth + flowWidth + height * 0.06f);

    using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    using (var icon = SKBitmap.Decode(RenderIcon(512)))
        canvas.DrawBitmap(icon, new SKRect(0, 0, iconSize, iconSize), new SKPaint { IsAntialias = true });

    var baseline = height * 0.5f - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
    using var ink = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#EEF2F8") };
    canvas.DrawText(sub, iconSize + gap, baseline, SKTextAlign.Left, font, ink);
    using var accent = new SKPaint { IsAntialias = true, Color = subtitle };
    canvas.DrawText(flowText, iconSize + gap + subWidth, baseline, SKTextAlign.Left, font, accent);

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// ICO with PNG-compressed entries (supported since Windows Vista).
void WriteIco(string path, IReadOnlyList<int> sizes)
{
    var images = sizes.Select(size => (Size: size, Png: RenderIcon(size))).ToList();
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)images.Count);
    var offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(png.Length);
        writer.Write(offset);
        offset += png.Length;
    }

    foreach (var (_, png) in images)
        writer.Write(png);
}

WriteIco(Path.Combine(outputDirectory, "subflow.ico"), [16, 20, 24, 32, 40, 48, 64, 128, 256]);
File.WriteAllBytes(Path.Combine(outputDirectory, "subflow-256.png"), RenderIcon(256));
File.WriteAllBytes(Path.Combine(outputDirectory, "subflow-512.png"), RenderIcon(512));
File.WriteAllBytes(Path.Combine(outputDirectory, "subflow-tray.png"), RenderIcon(32));
File.WriteAllBytes(Path.Combine(outputDirectory, "subflow-wordmark.png"), RenderWordmark(96));
Console.WriteLine("Brand assets written to " + Path.GetFullPath(outputDirectory));
