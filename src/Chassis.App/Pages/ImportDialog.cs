using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Chassis.App.Services;
using Chassis.App.Theming;
using Chassis.App.ViewModels;
using Chassis.Core.Catalog;
using Chassis.Core.Import;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Chassis.App.Pages;

/// <summary>
/// "Build from specs": add a screenshot or photo of a parts list (or paste its text). Windows reads the text on this PC,
/// Chassis matches each line to a part, you review the list, and the parts go into the build, inside the case you chose.
/// </summary>
public sealed class ImportDialog
{
    readonly BuildSession _s;
    readonly ContentDialog _dialog;
    readonly TextBox _text = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 132, FontFamily = Ui.Mono, FontSize = 12,
        PlaceholderText = "…or paste the text of a parts list here (PCPartPicker, a store page, Task Manager, a spec sheet)",
        CornerRadius = Ui.R,
    };
    readonly Image _preview = new() { MaxHeight = 150, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _previewBox;
    readonly TextBlock _status = Ui.Muted("", 13);
    readonly ProgressRing _ring = new() { Width = 18, Height = 18, IsActive = false };
    readonly StackPanel _results = new() { Spacing = 6 };
    readonly CheckBox _keepCase = new() { IsChecked = true };
    readonly TextBlock _caseNote = Ui.Muted("", 13);
    readonly Dictionary<Category, CheckBox> _checks = new();
    ImportResult? _result;

    public ImportDialog(BuildSession session, XamlRoot root)
    {
        _s = session;
        _previewBox = new Border { Child = _preview, CornerRadius = Ui.R, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
        var choose = Ui.Ghost("Choose image…", () => _ = PickAsync(), "\uEB9F");
        var paste = Ui.Ghost("Paste", () => _ = PasteAsync(), "\uE77F");
        var buttons = Ui.Row(8, choose, paste, _ring, _status);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _ring.VerticalAlignment = VerticalAlignment.Center;
        _text.TextChanged += (_, _) => Rematch();
        _keepCase.Checked += (_, _) => RenderCaseNote();
        _keepCase.Unchecked += (_, _) => RenderCaseNote();

        var body = Ui.Column(14,
            Ui.Text("Add a screenshot or photo of a parts list, or paste its text. Chassis reads it on this PC (nothing is uploaded), finds each part, and builds it inside your case. You can also drop a picture straight onto the 3D view.", 14),
            buttons, _previewBox, _text,
            Ui.Label("FOUND IN YOUR LIST"), _results, _keepCase, _caseNote);

        _dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Build from a spec list",
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 14, 0) },
            PrimaryButtonText = "Build it",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            RequestedTheme = ThemeBrushes.ElementTheme,
            Background = ThemeBrushes.Surface,
            CornerRadius = Ui.R,
        };
        _dialog.Resources["ContentDialogMaxWidth"] = 900.0;
        _dialog.Resources["ContentDialogMinWidth"] = 720.0;
        _dialog.PrimaryButtonClick += (_, _) => Apply();
        Rematch();
    }

    public Task ShowAsync() => _dialog.ShowAsync().AsTask();
    public string RecognizedText => _text.Text;
    public IReadOnlyList<SpecMatch> Found => _result?.Matches ?? [];
    public void Hide() => _dialog.Hide();

    /// <summary>Puts the found parts into the build (the tour and the Build it button use this).</summary>
    public void Apply()
    {
        if (_result is null) return;
        var parts = _result.Matches
            .Where(m => _checks.TryGetValue(m.Category, out var cb) && cb.IsChecked == true)
            .Where(m => !(m.Category == Category.Case && _keepCase.IsChecked == true && _s.Build[Category.Case] is not null))
            .Select(m => m.Part).ToList();
        _s.ApplyParts(parts);
    }

    public async Task LoadImageAsync(byte[] bytes, string source)
    {
        try
        {
            _ring.IsActive = true;
            _status.Text = $"Reading {source}…";
            using (var s = new InMemoryRandomAccessStream())
            {
                await s.WriteAsync(bytes.AsBuffer());
                s.Seek(0);
                var bmp = new BitmapImage();
                await bmp.SetSourceAsync(s);
                _preview.Source = bmp;
                _previewBox.Visibility = Visibility.Visible;
            }
            var text = await SpecReader.ReadAsync(bytes);
            _text.Text = text;   // shown so it can be corrected; matching re-runs as you type
            Rematch();
            var n = _result?.Matches.Count ?? 0;
            _status.Text = text.Trim().Length == 0 ? "Couldn't find any text in that picture. Try a sharper or closer shot."
                : n == 0 ? "Read the text, but didn't recognize any parts. You can fix the text below."
                : $"Found {n} part{(n == 1 ? "" : "s")}. Fix the text below if anything was misread.";
        }
        catch (Exception e)
        {
            CrashLog.Write("import read", e, show: false);
            _status.Text = e is InvalidOperationException ? e.Message : "Couldn't open that picture. PNG, JPG and BMP work best.";
        }
        finally { _ring.IsActive = false; }
    }

    public void SetText(string text) { _text.Text = text; Rematch(); }

    async Task PickAsync()
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" }) picker.FileTypeFilter.Add(ext);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Hwnd);
            if (await picker.PickSingleFileAsync() is not { } file) return;
            await LoadFileAsync(file);
        }
        catch (Exception e) { CrashLog.Write("import pick", e, show: false); _status.Text = "Couldn't open the file picker."; }
    }

    public async Task LoadFileAsync(StorageFile file)
    {
        using var s = await file.OpenReadAsync();
        await LoadImageAsync(await SpecReader.BytesAsync(s), file.Name);
    }

    async Task PasteAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Bitmap))
            {
                var r = await content.GetBitmapAsync();
                using var s = await r.OpenReadAsync();
                await LoadImageAsync(await SpecReader.BytesAsync(s), "the pasted picture");
            }
            else if (content.Contains(StandardDataFormats.StorageItems) && (await content.GetStorageItemsAsync()).OfType<StorageFile>().FirstOrDefault() is { } f)
                await LoadFileAsync(f);
            else if (content.Contains(StandardDataFormats.Text))
                SetText(await content.GetTextAsync());
            else
                _status.Text = "The clipboard is empty. Copy a screenshot (Win+Shift+S) or some text first.";
        }
        catch (Exception e) { CrashLog.Write("import paste", e, show: false); _status.Text = "Couldn't read the clipboard."; }
    }

    void Rematch()
    {
        var keep = new Dictionary<Category, bool>();
        foreach (var (c, cb) in _checks) keep[c] = cb.IsChecked == true;
        _result = SpecMatcher.Match(_text.Text, _s.Catalog);
        _results.Children.Clear();
        _checks.Clear();
        if (_result.Matches.Count == 0)
            _results.Children.Add(Ui.Muted(_text.Text.Trim().Length == 0 ? "Nothing yet. Add a picture or paste some text." : "No parts recognized yet.", 13));
        foreach (var m in _result.Matches)
        {
            var row = Ui.Columns(Ui.Px(120), Ui.Star(), Ui.Auto);
            row.Children.Add(Ui.Label(Categories.Label(m.Category).ToUpperInvariant()).At(0));
            var name = Ui.Column(1, Ui.Text(m.Part.FullName, 14, null, FontWeights.SemiBold),
                Ui.Label("from: " + (m.Evidence.Length > 90 ? m.Evidence[..90] + "…" : m.Evidence)));
            row.Children.Add(name.At(1));
            var (label, bg, fg) = m.Strength switch
            {
                MatchStrength.Exact => ("exact", ThemeBrushes.GoodBg, ThemeBrushes.Good),
                MatchStrength.Likely => ("close match", ThemeBrushes.Surface2, ThemeBrushes.Text),
                _ => ("best guess", ThemeBrushes.BadBg, ThemeBrushes.Warn),
            };
            row.Children.Add(Ui.Pill(label, bg, fg).At(2));
            var cb = new CheckBox { Content = row, IsChecked = keep.TryGetValue(m.Category, out var k) ? k : true, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
            cb.Checked += (_, _) => UpdateButton();
            cb.Unchecked += (_, _) => UpdateButton();
            _checks[m.Category] = cb;
            _results.Children.Add(cb);
        }
        var missing = _result.NotFound.Where(c => c != Category.Case).ToList();
        if (_result.Matches.Count > 0 && missing.Count > 0)
            _results.Children.Add(Ui.Muted("Not in the list: " + string.Join(", ", missing.Select(c => Categories.Label(c).ToLowerInvariant())) + ". Your current picks stay for those.", 12));
        RenderCaseNote();
        UpdateButton();
    }

    void RenderCaseNote()
    {
        var mine = _s.Resolved.Case;
        var found = _result?.For(Category.Case);
        _keepCase.Visibility = mine is not null && found is not null && found.Part.Id != mine.Id ? Visibility.Visible : Visibility.Collapsed;
        _keepCase.Content = mine is null ? "" : $"Keep my case ({mine.FullName}) instead of the one in the list";
        var inside = _keepCase.Visibility == Visibility.Visible
            ? (_keepCase.IsChecked == true ? mine : found!.Part)
            : found?.Part ?? mine;
        _caseNote.Text = inside is null
            ? "There's no case yet. Pick one after and Chassis will check that everything fits."
            : $"Everything goes inside the {inside.FullName}. Chassis checks the fit as soon as you build it.";
    }

    void UpdateButton() => _dialog.IsPrimaryButtonEnabled = _checks.Values.Any(c => c.IsChecked == true);
}
