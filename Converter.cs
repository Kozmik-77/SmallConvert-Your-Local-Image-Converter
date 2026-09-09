using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ImageMagick;
using Markdig;
using SkiaSharp;
using Svg.Skia;
using System.Text;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WP = DocumentFormat.OpenXml.Wordprocessing;

namespace SmallConvert;

public enum Target { Jpg, Png, Pdf, Docx }

public static class Converter
{
    public static readonly (string Name, Target Target, string Ext)[] Targets =
    {
        ("JPG",          Target.Jpg,  ".jpg"),
        ("PNG",          Target.Png,  ".png"),
        ("PDF",          Target.Pdf,  ".pdf"),
        ("Word (DOCX)",  Target.Docx, ".docx"),
    };

    private static readonly string[] ImageExts =
        { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff" };
    private static readonly string[] WordExts = { ".docx", ".doc" };
    private static readonly string[] MarkdownExts = { ".md", ".markdown" };
    private const string SvgExt = ".svg";

    private const float SvgRenderScale = 2.0f;
    private const int SvgMaxPixels = 6000;

    /// <summary>
    /// Converts every file; reports each result through <paramref name="log"/>
    /// using "[OK] ...", "[SKIP] ...", "[FAIL] ..." or "[INFO] ..." lines.
    /// Safe to call from a background thread.
    /// </summary>
    public static (int Ok, int Skipped, int Failed) ConvertBatch(
        IReadOnlyList<string> files, Target target, Action<string> log)
    {
        var targetExt = Targets.First(t => t.Target == target).Ext;
        var targetName = Targets.First(t => t.Target == target).Name;
        int ok = 0, skipped = 0, failed = 0;

        IDocumentEngine? engine = null;
        bool engineLookedUp = false;

        try
        {
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var name = Path.GetFileName(file);

                bool isImage = ImageExts.Contains(ext);
                bool isSvg = ext == SvgExt;
                bool isPdf = ext == ".pdf";
                bool isWord = WordExts.Contains(ext);
                bool isMarkdown = MarkdownExts.Contains(ext);
                bool isDocument = isPdf || isWord || isMarkdown;

                if (!isImage && !isSvg && !isDocument)
                {
                    log($"[SKIP] {name} - unsupported file type");
                    skipped++;
                    continue;
                }

                bool sameFormat = target switch
                {
                    Target.Jpg  => ext is ".jpg" or ".jpeg",
                    Target.Png  => ext == ".png",
                    Target.Pdf  => isPdf,
                    Target.Docx => isWord,
                    _ => false
                };
                if (sameFormat)
                {
                    log($"[SKIP] {name} - already {targetName}");
                    skipped++;
                    continue;
                }

                if (isDocument && target is Target.Jpg or Target.Png)
                {
                    log($"[SKIP] {name} - document to image is not supported");
                    skipped++;
                    continue;
                }

                try
                {
                    var output = UniquePath(Path.ChangeExtension(file, targetExt));

                    if (isDocument)
                    {
                        if (!engineLookedUp)
                        {
                            engine = DocumentEngines.Detect();
                            engineLookedUp = true;
                            if (engine is not null) log($"[INFO] Using {engine.Name} for document conversion");
                        }
                        if (engine is null)
                            throw new InvalidOperationException(
                                "neither Microsoft Word nor LibreOffice was found (needed for PDF, Word and Markdown)");

                        if (isMarkdown)
                            ConvertMarkdown(engine, file, output, target);
                        else
                            engine.Convert(file, isPdf ? DocKind.Pdf : DocKind.Word, output, target);
                    }
                    else
                    {
                        using var image = isSvg ? RenderSvg(file) : new MagickImage(file);
                        switch (target)
                        {
                            case Target.Jpg:
                                image.BackgroundColor = MagickColors.White;
                                image.Alpha(AlphaOption.Remove);
                                image.Quality = 92;
                                image.Write(output, MagickFormat.Jpeg);
                                break;
                            case Target.Png:
                                image.Write(output, MagickFormat.Png);
                                break;
                            case Target.Pdf:
                                image.Write(output, MagickFormat.Pdf);
                                break;
                            case Target.Docx:
                                WriteDocxWithImage(image, output);
                                break;
                        }
                    }

                    log($"[OK] {name} -> {Path.GetFileName(output)}");
                    ok++;
                }
                catch (Exception ex)
                {
                    log($"[FAIL] {name} - {ex.Message}");
                    failed++;
                }
            }
        }
        finally
        {
            engine?.Dispose();
        }

        return (ok, skipped, failed);
    }

    // ── SVG rasterisation via Skia ───────────────────────────────────────────

    private static MagickImage RenderSvg(string path)
    {
        using var svg = new SKSvg();
        var picture = svg.Load(path)
            ?? throw new InvalidOperationException("SVG could not be read");

        var rect = picture.CullRect;
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("SVG has no usable dimensions");

        float scale = SvgRenderScale;
        float longest = Math.Max(rect.Width, rect.Height) * scale;
        if (longest > SvgMaxPixels) scale *= SvgMaxPixels / longest;

        int w = Math.Max(1, (int)Math.Ceiling(rect.Width * scale));
        int h = Math.Max(1, (int)Math.Ceiling(rect.Height * scale));

        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-rect.Left, -rect.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return new MagickImage(data.ToArray());
    }

    // ── Markdown -> HTML -> engine ───────────────────────────────────────────

    private static readonly MarkdownPipeline MdPipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static void ConvertMarkdown(IDocumentEngine engine, string input, string output, Target target)
    {
        var markdown = File.ReadAllText(input, Encoding.UTF8);
        var body = Markdown.ToHtml(markdown, MdPipeline);

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(input))!;
        var baseHref = new Uri(baseDir + Path.DirectorySeparatorChar).AbsoluteUri;

        var html =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
            $"<base href=\"{baseHref}\">" +
            "<style>" +
            "body{font-family:Calibri,Carlito,Arial,sans-serif;font-size:11pt;line-height:1.4;}" +
            "h1{font-size:20pt;} h2{font-size:16pt;} h3{font-size:13pt;}" +
            "code,pre{font-family:'Courier New',monospace;font-size:10pt;}" +
            "pre{background:#f2f2f2;padding:6pt;border:1px solid #ccc;}" +
            "blockquote{border-left:3px solid #999;margin-left:0;padding-left:8pt;color:#555;}" +
            "table{border-collapse:collapse;} td,th{border:1px solid #999;padding:3pt 6pt;}" +
            "</style></head><body>" + body + "</body></html>";

        // Keep the original stem so LibreOffice's output name is predictable
        var tempDir = Path.Combine(Path.GetTempPath(), "smallconvert-md-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var temp = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(input) + ".html");
        File.WriteAllText(temp, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            engine.Convert(temp, DocKind.Html, output, target);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ── Image -> DOCX (embed picture, fit to page) ───────────────────────────

    private static void WriteDocxWithImage(MagickImage image, string outputPath)
    {
        using var pngStream = new MemoryStream();
        image.Write(pngStream, MagickFormat.Png);
        pngStream.Position = 0;

        const long EmuPerInch = 914400L;
        const double MaxWidthInches = 6.3;
        const double MaxHeightInches = 9.0;

        double dpi = image.Density.X > 0 ? image.Density.X : 96.0;
        double wIn = image.Width / dpi;
        double hIn = image.Height / dpi;

        double scale = Math.Min(1.0, Math.Min(MaxWidthInches / wIn, MaxHeightInches / hIn));
        long cx = (long)(wIn * scale * EmuPerInch);
        long cy = (long)(hIn * scale * EmuPerInch);

        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new WP.Document(new WP.Body());

        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        imagePart.FeedData(pngStream);
        var relId = mainPart.GetIdOfPart(imagePart);

        var drawing = new WP.Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.DocProperties { Id = 1U, Name = "Image" },
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "Image" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
            ) { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });

        mainPart.Document.Body!.Append(new WP.Paragraph(new WP.Run(drawing)));
        mainPart.Document.Save();
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
