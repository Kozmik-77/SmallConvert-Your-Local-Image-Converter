using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace SmallConvert;

public class MainWindow : Window
{
    private readonly ComboBox _targetBox;
    private readonly Border _dropZone;
    private readonly TextBlock _dropLabel;
    private readonly StackPanel _logPanel;
    private readonly ScrollViewer _logScroll;
    private readonly TextBlock _statusLabel;

    private bool _busy;

    // Classic palette
    private static readonly IBrush Bg         = Brushes.Black;
    private static readonly IBrush PanelBg    = Brushes.Black;
    private static readonly IBrush DropActive = new SolidColorBrush(Color.FromRgb(0, 0, 96));
    private static readonly IBrush Border3D   = new SolidColorBrush(Color.FromRgb(110, 110, 110));
    private static readonly IBrush TextMain   = Brushes.Silver;
    private static readonly IBrush TextDim    = Brushes.Gray;
    private static readonly IBrush OkGreen    = Brushes.Lime;
    private static readonly IBrush WarnYellow = Brushes.Yellow;
    private static readonly IBrush FailRed    = Brushes.Red;
    private static readonly IBrush NoteWhite  = Brushes.White;

    private static readonly FontFamily UiFont  = new("Tahoma, Segoe UI, Helvetica Neue, DejaVu Sans, sans-serif");
    private static readonly FontFamily LogFont = new("Courier New, Menlo, Liberation Mono, DejaVu Sans Mono, monospace");

    private const double LogLineSpacing = 5;

    public MainWindow()
    {
        Title = "SmallConvert";
        Width = 860;
        Height = 640;
        MinWidth = 760;
        MinHeight = 580;
        Background = Bg;
        FontFamily = UiFont;
        FontSize = 12;

        var root = new Grid
        {
            Margin = new Thickness(12, 10, 12, 8),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
        };

        // ── Title ───────────────────────────────────────────────────────────
        var title = new TextBlock
        {
            Text = "SmallConvert",
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            FontStyle = FontStyle.Italic,
            Foreground = NoteWhite,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(title, 0);

        // ── Target picker ───────────────────────────────────────────────────
        var pickerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 10),
        };
        pickerRow.Children.Add(new TextBlock
        {
            Text = "Convert to:",
            Foreground = TextMain,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _targetBox = new ComboBox
        {
            ItemsSource = Converter.Targets.Select(t => t.Name).ToList(),
            SelectedIndex = 0,
            Width = 160,
            Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
            Foreground = NoteWhite,
        };
        _targetBox.SelectionChanged += (_, _) => UpdateDropHint();
        pickerRow.Children.Add(_targetBox);
        Grid.SetRow(pickerRow, 1);

        // ── Instructions ────────────────────────────────────────────────────
        var instructions = new Border
        {
            BorderBrush = Border3D,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = "How to use", Foreground = TextMain, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) },
                    Line("1. Choose the target format.  2. Drag files onto the drop area on the right. Input format is detected automatically."),
                    Line("Supported input: PNG, JPG, WebP, BMP, GIF, TIFF, SVG, PDF, DOC/DOCX, MD.  Output is saved in the same folder as the original."),
                    Line("Existing files are never overwritten - a \"(1)\" suffix is added instead. JPG output turns transparency white."),
                    Line("PDF, Word and Markdown conversions need Microsoft Word (Windows) or LibreOffice (all platforms) to be installed."),
                }
            }
        };
        Grid.SetRow(instructions, 2);

        // ── Headers for the two panes ───────────────────────────────────────
        var headers = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1*,2*"),
            Margin = new Thickness(0, 0, 0, 3),
        };
        var logHeader = new TextBlock { Text = "Log:", Foreground = TextMain };
        var dropHeader = new TextBlock { Text = "Drop area:", Foreground = TextMain, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(logHeader, 0);
        Grid.SetColumn(dropHeader, 1);
        headers.Children.Add(logHeader);
        headers.Children.Add(dropHeader);
        Grid.SetRow(headers, 3);

        // ── Log (left, 1/3) + drop zone (right, 2/3) ─────────────────────────
        var panes = new Grid { ColumnDefinitions = new ColumnDefinitions("1*,2*") };

        _logPanel = new StackPanel { Margin = new Thickness(4) };
        _logScroll = new ScrollViewer
        {
            Content = _logPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var logBorder = new Border
        {
            BorderBrush = Border3D,
            BorderThickness = new Thickness(1),
            Background = PanelBg,
            Margin = new Thickness(0, 0, 4, 0),
            Child = _logScroll,
        };
        Grid.SetColumn(logBorder, 0);

        _dropLabel = new TextBlock
        {
            Foreground = TextDim,
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _dropZone = new Border
        {
            BorderBrush = Border3D,
            BorderThickness = new Thickness(1),
            Background = PanelBg,
            Margin = new Thickness(4, 0, 0, 0),
            Child = _dropLabel,
        };
        Grid.SetColumn(_dropZone, 1);

        DragDrop.SetAllowDrop(_dropZone, true);
        _dropZone.AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        _dropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        _dropZone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => _dropZone.Background = PanelBg);
        _dropZone.AddHandler(DragDrop.DropEvent, OnDrop);

        panes.Children.Add(logBorder);
        panes.Children.Add(_dropZone);
        Grid.SetRow(panes, 4);

        // ── Status bar ──────────────────────────────────────────────────────
        _statusLabel = new TextBlock { Text = "Ready.", Foreground = TextMain, Padding = new Thickness(6, 3) };
        var status = new Border
        {
            BorderBrush = Border3D,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 0),
            Child = _statusLabel,
        };
        Grid.SetRow(status, 5);

        root.Children.Add(title);
        root.Children.Add(pickerRow);
        root.Children.Add(instructions);
        root.Children.Add(headers);
        root.Children.Add(panes);
        root.Children.Add(status);
        Content = root;

        UpdateDropHint();
        AddLog("[INFO] Ready. Choose a target format and drop files.");
    }

    private static TextBlock Line(string text) => new()
    {
        Text = text,
        Foreground = TextDim,
        TextWrapping = TextWrapping.Wrap,
    };

    private Target CurrentTarget => Converter.Targets[Math.Max(0, _targetBox.SelectedIndex)].Target;
    private string CurrentTargetName => Converter.Targets[Math.Max(0, _targetBox.SelectedIndex)].Name;

    private void UpdateDropHint() =>
        _dropLabel.Text = $"Drop files here\n\n[ output: {CurrentTargetName} ]";

    // ── Log ─────────────────────────────────────────────────────────────────
    // [OK] green, [SKIP] yellow, [FAIL] red, everything else ([INFO]) white.

    private void AddLog(string line)
    {
        var color = line.StartsWith("[OK]")   ? OkGreen
                  : line.StartsWith("[SKIP]") ? WarnYellow
                  : line.StartsWith("[FAIL]") ? FailRed
                  : NoteWhite;

        _logPanel.Children.Add(new TextBlock
        {
            Text = line,
            Foreground = color,
            FontFamily = LogFont,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, LogLineSpacing),
        });
        _logScroll.ScrollToEnd();
    }

    // ── Drag & drop ─────────────────────────────────────────────────────────

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool ok = !_busy && e.Data.Contains(DataFormats.Files);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        _dropZone.Background = ok ? DropActive : PanelBg;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        _dropZone.Background = PanelBg;
        if (_busy) return;

        var files = e.Data.GetFiles()?
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
        if (files is null || files.Count == 0) return;

        var target = CurrentTarget;
        var targetName = CurrentTargetName;

        _busy = true;
        _statusLabel.Text = "Converting...";
        _statusLabel.Foreground = TextMain;
        AddLog($"[INFO] Converting {files.Count} file(s) to {targetName}...");

        try
        {
            // Run off the UI thread so the window stays responsive
            var result = await Task.Run(() =>
                Converter.ConvertBatch(files, target,
                    line => Dispatcher.UIThread.Post(() => AddLog(line))));

            _statusLabel.Text = $"Done - {result.Ok} converted, {result.Skipped} skipped, {result.Failed} failed.";
            _statusLabel.Foreground = result.Failed > 0 ? FailRed : OkGreen;
        }
        catch (Exception ex)
        {
            AddLog($"[FAIL] Unexpected error - {ex.Message}");
            _statusLabel.Text = "Error.";
            _statusLabel.Foreground = FailRed;
        }
        finally
        {
            _busy = false;
        }
    }
}
