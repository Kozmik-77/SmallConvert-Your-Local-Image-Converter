using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ImageMagick;
using Markdig;
using SkiaSharp;
using Svg.Skia;
using System.Runtime.InteropServices;
using System.Text;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WP = DocumentFormat.OpenXml.Wordprocessing;

namespace SmallConvert;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public class MainForm : Form
{
    private readonly ComboBox _targetBox;
    private readonly Panel _dropZone;
    private readonly Label _dropLabel;
    private readonly ListBox _log;
    private readonly Label _statusLabel;

    private enum Target { Jpg, Png, Pdf, Docx }

    private static readonly (string Name, Target Target, string Ext)[] Targets =
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

  
    // capped so absurdly large drawings don't exhaust memory.
    private const float SvgRenderScale = 2.0f;
    private const int SvgMaxPixels = 6000;

    // palette
    private static readonly Color Bg        = Color.Black;
    private static readonly Color PanelBg   = Color.Black;
    private static readonly Color TextMain  = Color.Silver;
    private static readonly Color TextDim   = Color.Gray;
    private static readonly Color OkGreen   = Color.Lime;
    private static readonly Color WarnYellow= Color.Yellow;
    private static readonly Color FailRed   = Color.Red;
    private static readonly Color NoteWhite = Color.White;

    public MainForm()
    {
        Text = "SmallConvert";
        MinimumSize = new Size(760, 600);
        Size = new Size(860, 660);
        BackColor = Bg;
        ForeColor = TextMain;
        Font = new Font("Tahoma", 8.25f);   
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(6F, 13F);   

        //title, target picker, instructions

        var top = new Panel { Dock = DockStyle.Top, Height = 236, BackColor = Bg };

        var title = new Label
        {
            Text = "SmallConvert",
            Font = new Font("Tahoma", 12f, FontStyle.Bold | FontStyle.Italic),
            ForeColor = NoteWhite,
            AutoSize = true,
            Location = new Point(12, 10)
        };

        var modeLabel = new Label
        {
            Text = "Convert to:",
            ForeColor = TextMain,
            AutoSize = true,
            Location = new Point(14, 50)
        };

        _targetBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.System,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = NoteWhite,
            Location = new Point(130, 46),
            Width = 140
        };
        foreach (var t in Targets) _targetBox.Items.Add(t.Name);
        _targetBox.SelectedIndex = 0;
        _targetBox.SelectedIndexChanged += (_, _) => UpdateDropHint();

        var instructions = new GroupBox
        {
            Text = " How to use ",
            ForeColor = TextMain,
            Location = new Point(12, 82),
            Size = new Size(820, 146),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var instructionsText = new Label
        {
            Text = "1. Choose the target format.  2. Drag files onto the drop area on the right. Input format is detected automatically.\n" +
                   "Supported input: PNG, JPG, WebP, BMP, GIF, TIFF, SVG, PDF, DOC/DOCX, MD.  Output is saved in the same folder as the original.\n" +
                   "Existing files are never overwritten - a \"(1)\" suffix is added instead. JPG output turns transparency white.\n" +
                   "PDF, Word and Markdown conversions require Microsoft Word to be installed on this computer.",
            ForeColor = TextDim,
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 6, 10, 4)
        };
        instructions.Controls.Add(instructionsText);
        top.Controls.AddRange(new Control[] { title, modeLabel, _targetBox, instructions });

        // status bar

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            BackColor = Bg,
            BorderStyle = BorderStyle.Fixed3D
        };
        _statusLabel = new Label
        {
            Text = "Ready.",
            ForeColor = TextMain,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        bottom.Controls.Add(_statusLabel);

        // log + drop zone

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(8, 0, 8, 4),
            BackColor = Bg
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66.7f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var logHeader = new Label
        {
            Text = "Log:",
            ForeColor = TextMain,
            TextAlign = ContentAlignment.BottomLeft,
            Dock = DockStyle.Fill
        };
        var dropHeader = new Label
        {
            Text = "Drop area:",
            ForeColor = TextMain,
            TextAlign = ContentAlignment.BottomLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 0, 0, 0)
        };

        _log = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBg,
            BorderStyle = BorderStyle.Fixed3D,      
            Font = new Font("Courier New", 8.25f),  
            IntegralHeight = false,
            HorizontalScrollbar = false,            
            DrawMode = DrawMode.OwnerDrawVariable,
            Margin = new Padding(0, 2, 6, 4)
        };
        _log.MeasureItem += Log_MeasureItem;
        _log.DrawItem += Log_DrawItem;
        _log.Resize += (_, _) => RemeasureLog();

        _dropZone = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBg,
            BorderStyle = BorderStyle.Fixed3D,   
            AllowDrop = true,
            Margin = new Padding(6, 2, 0, 4)
        };
        _dropZone.DragEnter += DropZone_DragEnter;
        _dropZone.DragLeave += (_, _) => SetDropActive(false);
        _dropZone.DragDrop += DropZone_DragDrop;

        _dropLabel = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = TextDim,
            Font = new Font("Tahoma", 9.75f),
            BackColor = Color.Transparent,
            AllowDrop = true
        };
        _dropLabel.DragEnter += DropZone_DragEnter;
        _dropLabel.DragLeave += (_, _) => SetDropActive(false);
        _dropLabel.DragDrop += DropZone_DragDrop;
        _dropZone.Controls.Add(_dropLabel);

        grid.Controls.Add(logHeader, 0, 0);
        grid.Controls.Add(dropHeader, 1, 0);
        grid.Controls.Add(_log, 0, 1);
        grid.Controls.Add(_dropZone, 1, 1);

        Controls.Add(grid);
        Controls.Add(top);
        Controls.Add(bottom);

        UpdateDropHint();
        AddLog("[INFO] Ready. Choose a target format and drop files.");
    }

    
    // [OK] green, [SKIP] yellow, [FAIL] red, everything else ([INFO]) white.

    private const int LogLineSpacing = 5; 

    
    private readonly List<string> _logLines = new();

    private void AddLog(string line)
    {
        _logLines.Add(line);
        _log.Items.Add(line);
        _log.TopIndex = _log.Items.Count - 1;
    }

    private string LineAt(int index) =>
        index >= 0 && index < _logLines.Count ? _logLines[index] : string.Empty;

    private int LogTextWidth => Math.Max(40, _log.ClientSize.Width - 8);

    private static TextFormatFlags LogFlags =>
        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left;

    private void Log_MeasureItem(object? sender, MeasureItemEventArgs e)
    {
        var text = LineAt(e.Index);
        if (text.Length == 0)
        {
            e.ItemHeight = _log.Font.Height + LogLineSpacing;
            return;
        }

        var size = TextRenderer.MeasureText(
            e.Graphics, text, _log.Font, new Size(LogTextWidth, int.MaxValue), LogFlags);
        e.ItemHeight = size.Height + LogLineSpacing;
    }

    private void Log_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var text = LineAt(e.Index);
        e.DrawBackground();
        if (text.Length == 0) return;

        var color = text.StartsWith("[OK]")   ? OkGreen
                  : text.StartsWith("[SKIP]") ? WarnYellow
                  : text.StartsWith("[FAIL]") ? FailRed
                  : NoteWhite;

        var bounds = new Rectangle(
            e.Bounds.X + 2,
            e.Bounds.Y + LogLineSpacing / 2,
            LogTextWidth,
            e.Bounds.Height - LogLineSpacing / 2);
        TextRenderer.DrawText(e.Graphics, text, _log.Font, bounds, color, LogFlags);
    }

    // readd item heights after resize
    private void RemeasureLog()
    {
        if (_logLines.Count == 0) return;
        _log.BeginUpdate();
        _log.Items.Clear();
        foreach (var line in _logLines) _log.Items.Add(line);
        _log.EndUpdate();
        _log.TopIndex = _log.Items.Count - 1;
    }

    private (string Name, Target Target, string Ext) CurrentTarget => Targets[_targetBox.SelectedIndex];

    private void UpdateDropHint()
    {
        _dropLabel.Text = $"Drop files here\n\n[ output: {CurrentTarget.Name} ]";
    }

    private bool _dropActive;
    private void SetDropActive(bool active)
    {
        if (_dropActive == active) return;
        _dropActive = active;
       
        _dropZone.BackColor = active ? Color.FromArgb(0, 0, 96) : PanelBg;
    }

    private void DropZone_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            SetDropActive(true);
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private void DropZone_DragDrop(object? sender, DragEventArgs e)
    {
        SetDropActive(false);
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        var target = CurrentTarget;
        int ok = 0, skipped = 0, failed = 0;

        AddLog($"[INFO] Converting {files.Length} file(s) to {target.Name}...");

        dynamic? word = null;   // one Word instance shared for the whole batch
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

                if (!isImage && !isSvg && !isPdf && !isWord && !isMarkdown)
                {
                    AddLog($"[SKIP] {name} - unsupported file type");
                    skipped++;
                    continue;
                }

                // Already in the target format?
                bool sameFormat = target.Target switch
                {
                    Target.Jpg  => ext is ".jpg" or ".jpeg",
                    Target.Png  => ext is ".png",
                    Target.Pdf  => isPdf,
                    Target.Docx => ext == ".docx",
                    _ => false
                };
                if (sameFormat)
                {
                    AddLog($"[SKIP] {name} - already {target.Name}");
                    skipped++;
                    continue;
                }

                // Document sources can only go to document targets
                if ((isPdf || isWord || isMarkdown) && target.Target is Target.Jpg or Target.Png)
                {
                    AddLog($"[SKIP] {name} - document to image is not supported");
                    skipped++;
                    continue;
                }
                if (isWord && target.Target == Target.Docx)
                {
                    AddLog($"[SKIP] {name} - already a Word document");
                    skipped++;
                    continue;
                }

                try
                {
                    var output = UniquePath(Path.ChangeExtension(file, target.Ext));

                    if (isPdf && target.Target == Target.Docx)
                    {
                        word ??= CreateWordApp();
                        ConvertWithWord(word, file, output, toPdf: false);
                    }
                    else if (isWord && target.Target == Target.Pdf)
                    {
                        word ??= CreateWordApp();
                        ConvertWithWord(word, file, output, toPdf: true);
                    }
                    else if (isMarkdown)
                    {
                        word ??= CreateWordApp();
                        ConvertMarkdownWithWord(word, file, output, toPdf: target.Target == Target.Pdf);
                    }
                    else
                    {
                        // Image or SVG source. SVG is rasterised first, then
                        // flows through exactly the same pipeline as a bitmap.
                        using var image = isSvg
                            ? RenderSvg(file)
                            : new MagickImage(file);

                        switch (target.Target)
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

                    AddLog($"[OK] {name} -> {Path.GetFileName(output)}");
                    ok++;
                }
                catch (Exception ex)
                {
                    AddLog($"[FAIL] {name} - {ex.Message}");
                    failed++;
                }
            }
        }
        finally
        {
            if (word is not null)
            {
                try { word.Quit(0); } catch { /* best effort */ }
                try { Marshal.ReleaseComObject(word); } catch { }
            }
        }

        _statusLabel.Text = $"Done - {ok} converted, {skipped} skipped, {failed} failed.";
        _statusLabel.ForeColor = failed > 0 ? FailRed : OkGreen;
    }

    // SVG rasterisation via Skia

    private static MagickImage RenderSvg(string path)
    {
        using var svg = new SKSvg();
        var picture = svg.Load(path);
        if (picture is null)
            throw new InvalidOperationException("SVG could not be read");

        var rect = picture.CullRect;
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("SVG has no usable dimensions");

        float scale = SvgRenderScale;
        float longest = Math.Max(rect.Width, rect.Height) * scale;
        if (longest > SvgMaxPixels)
            scale *= SvgMaxPixels / longest;

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

    // PDF <-> Word via Microsoft Word 
    // Requires Word to be installed

    private static dynamic CreateWordApp()
    {
        var type = Type.GetTypeFromProgID("Word.Application");
        if (type is null)
            throw new InvalidOperationException("Microsoft Word is not installed (required for PDF/Word conversion)");

        dynamic app = Activator.CreateInstance(type)!;
        app.Visible = false;
        app.DisplayAlerts = 0;   // wdAlertsNone
        return app;
    }

    private static void ConvertWithWord(dynamic word, string input, string output, bool toPdf)
    {
        dynamic doc = word.Documents.Open(input, ConfirmConversions: false, ReadOnly: true);
        try
        {
            if (toPdf)
                doc.ExportAsFixedFormat(output, 17);   // 17 = wdExportFormatPDF
            else
                doc.SaveAs2(output, 16);               // 16 = wdFormatDocumentDefault (.docx)
        }
        finally
        {
            doc.Close(0);                              // 0 = wdDoNotSaveChanges
            Marshal.ReleaseComObject(doc);
        }
    }

    // Markdown -> PDF / DOCX
   

    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static void ConvertMarkdownWithWord(dynamic word, string input, string output, bool toPdf)
    {
        var markdown = File.ReadAllText(input, Encoding.UTF8);
        var body = Markdown.ToHtml(markdown, MarkdownPipeline);

        // <base> lets relative image links in the .md resolve against its own folder
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(input))!;
        var baseHref = new Uri(baseDir + Path.DirectorySeparatorChar).AbsoluteUri;

        var html =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
            $"<base href=\"{baseHref}\">" +
            "<style>" +
            "body{font-family:Calibri,Arial,sans-serif;font-size:11pt;line-height:1.4;}" +
            "h1{font-size:20pt;} h2{font-size:16pt;} h3{font-size:13pt;}" +
            "code,pre{font-family:'Courier New',monospace;font-size:10pt;}" +
            "pre{background:#f2f2f2;padding:6pt;border:1px solid #ccc;}" +
            "blockquote{border-left:3px solid #999;margin-left:0;padding-left:8pt;color:#555;}" +
            "table{border-collapse:collapse;} td,th{border:1px solid #999;padding:3pt 6pt;}" +
            "</style></head><body>" + body + "</body></html>";

        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".html");
        // BOM makes Word pick up UTF-8 reliably (umlauts, Turkish characters, etc.)
        File.WriteAllText(temp, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            ConvertWithWord(word, temp, output, toPdf);
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    // Image -> DOCX (embed picture, fit to page) 

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

    // Never overwrite an existing file: photo.pdf -> photo (1).pdf, ...
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
