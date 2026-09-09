using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SmallConvert;

/// <summary>What kind of document is being handed to the engine.</summary>
public enum DocKind { Pdf, Word, Html }

/// <summary>
/// Something that can turn PDF / Word / HTML into PDF or DOCX.
/// Two implementations: Microsoft Word (COM, Windows only) and
/// LibreOffice (command line, all platforms).
/// </summary>
public interface IDocumentEngine : IDisposable
{
    string Name { get; }
    void Convert(string input, DocKind kind, string output, Target target);
}

public static class DocumentEngines
{
    /// <summary>Picks the best available engine, or null if none is installed.</summary>
    public static IDocumentEngine? Detect()
    {
        if (OperatingSystem.IsWindows())
        {
            var word = WordComEngine.TryCreate();
            if (word is not null) return word;
        }

        var soffice = LibreOfficeEngine.FindExecutable();
        return soffice is not null ? new LibreOfficeEngine(soffice) : null;
    }
}

// ── Microsoft Word via COM (Windows only) ───────────────────────────────────

[SupportedOSPlatform("windows")]
public sealed class WordComEngine : IDocumentEngine
{
    private dynamic? _app;

    public string Name => "Microsoft Word";

    private WordComEngine(dynamic app) => _app = app;

    public static WordComEngine? TryCreate()
    {
        try
        {
            var type = Type.GetTypeFromProgID("Word.Application");
            if (type is null) return null;

            dynamic app = Activator.CreateInstance(type)!;
            app.Visible = false;
            app.DisplayAlerts = 0;   // wdAlertsNone
            return new WordComEngine(app);
        }
        catch
        {
            return null;
        }
    }

    public void Convert(string input, DocKind kind, string output, Target target)
    {
        if (_app is null) throw new ObjectDisposedException(nameof(WordComEngine));

        dynamic doc = _app.Documents.Open(input, ConfirmConversions: false, ReadOnly: true);
        try
        {
            if (target == Target.Pdf)
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

    public void Dispose()
    {
        if (_app is null) return;
        try { _app.Quit(0); } catch { /* best effort */ }
        try { Marshal.ReleaseComObject(_app); } catch { }
        _app = null;
    }
}

// ── LibreOffice headless (Windows, macOS, Linux) ────────────────────────────

public sealed class LibreOfficeEngine : IDocumentEngine
{
    private readonly string _soffice;
    private readonly string _profileDir;

    public string Name => "LibreOffice";

    public LibreOfficeEngine(string sofficePath)
    {
        _soffice = sofficePath;
        // A private user profile prevents clashes with an already-running
        // LibreOffice window (otherwise headless calls can silently do nothing).
        _profileDir = Path.Combine(Path.GetTempPath(), "smallconvert-lo-" + Environment.ProcessId);
        Directory.CreateDirectory(_profileDir);
    }

    public static string? FindExecutable()
    {
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            })
            {
                if (!string.IsNullOrEmpty(root))
                    candidates.Add(Path.Combine(root, "LibreOffice", "program", "soffice.exe"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/Applications/LibreOffice.app/Contents/MacOS/soffice");
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Applications", "LibreOffice.app", "Contents", "MacOS", "soffice"));
        }
        else
        {
            candidates.Add("/usr/bin/soffice");
            candidates.Add("/usr/bin/libreoffice");
            candidates.Add("/usr/local/bin/soffice");
            candidates.Add("/snap/bin/libreoffice");
            candidates.Add("/opt/libreoffice/program/soffice");
        }

        // Anything on PATH
        var names = OperatingSystem.IsWindows()
            ? new[] { "soffice.exe" }
            : new[] { "soffice", "libreoffice" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            foreach (var n in names)
                if (!string.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir, n));

        return candidates.FirstOrDefault(File.Exists);
    }

    public void Convert(string input, DocKind kind, string output, Target target)
    {
        var outDir = Path.Combine(_profileDir, "out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        var psi = new ProcessStartInfo
        {
            FileName = _soffice,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.ArgumentList.Add("-env:UserInstallation=" + new Uri(_profileDir).AbsoluteUri);
        psi.ArgumentList.Add("--headless");
        psi.ArgumentList.Add("--norestore");

        // Make sure the input opens in Writer (not Draw / Writer-Web)
        switch (kind)
        {
            case DocKind.Pdf:  psi.ArgumentList.Add("--infilter=writer_pdf_import"); break;
            case DocKind.Html: psi.ArgumentList.Add("--infilter=HTML (StarWriter)"); break;
        }

        psi.ArgumentList.Add("--convert-to");
        psi.ArgumentList.Add(target == Target.Pdf ? "pdf:writer_pdf_Export" : "docx");
        psi.ArgumentList.Add("--outdir");
        psi.ArgumentList.Add(outDir);
        psi.ArgumentList.Add(input);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start LibreOffice");
        var stderr = proc.StandardError.ReadToEnd();
        proc.StandardOutput.ReadToEnd();
        if (!proc.WaitForExit(120_000))
        {
            try { proc.Kill(true); } catch { }
            throw new TimeoutException("LibreOffice did not finish within 2 minutes");
        }

        var produced = Path.Combine(outDir,
            Path.GetFileNameWithoutExtension(input) + (target == Target.Pdf ? ".pdf" : ".docx"));

        if (!File.Exists(produced))
        {
            var detail = stderr.Trim();
            throw new InvalidOperationException(
                "LibreOffice produced no output" + (detail.Length > 0 ? ": " + detail : ""));
        }

        File.Move(produced, output, overwrite: false);
        try { Directory.Delete(outDir, recursive: true); } catch { }
    }

    public void Dispose()
    {
        try { Directory.Delete(_profileDir, recursive: true); } catch { /* best effort */ }
    }
}
