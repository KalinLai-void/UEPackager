using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace PackagingAutomation;

public sealed class MainWindow : Window
{
    readonly TextBox project = new(), engine = new(), destination = new(), logBox = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = FontFamily.Parse("Consolas, Menlo, monospace"), FontSize = 12 };
    readonly ComboBox flavor = new() { ItemsSource = new[] { "ASTC", "ETC2", "DXT", "Multi" }, SelectedIndex = 0, MinWidth = 140 };
    readonly StackPanel rows = new() { Spacing = 0 };
    readonly ScrollViewer platformScroll;
    readonly TextBlock status = new() { Text = "Ready", TextWrapping = TextWrapping.Wrap };
    readonly Button stop = new() { Content = "Stop", IsEnabled = false };
    readonly List<Control> inputs = [];
    readonly Dictionary<string, TextBlock> stateLabels = [];
    readonly List<Border> rowBorders = [];
    readonly SettingsStore store;
    readonly ConcurrentQueue<string> logQueue = new();
    readonly DispatcherTimer logTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly DispatcherTimer dragTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    List<Platform> platforms = [];
    BuildRunner? runner; bool busy, loading, closeWhenDone, allowClose;
    string loadedEngine = ""; int selectedIndex = -1, pressedRow = -1, dropGap = -1; bool dragging; Point pressPoint, lastDragPoint; IPointer? capturedPointer;

    public MainWindow(SettingsStore? settingsStore = null)
    {
        store = settingsStore ?? new SettingsStore();
        Title = "UEPackager"; Width = 1060; Height = 880; MinWidth = 820; MinHeight = 720; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        using var icon = AssetLoader.Open(new Uri("avares://UEPackager/Assets/App.ico")); Icon = new WindowIcon(icon);
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,*"), RowSpacing = 12 };
        root.Children.Add(PathField("Project (.uproject)", project, () => BrowseProject(), 0));
        root.Children.Add(PathField("Unreal Engine folder (contains Engine)", engine, () => BrowseFolder(engine), 1));
        var platformGroup = new StackPanel { Spacing = 8 };
        platformGroup.Children.Add(new TextBlock { Text = "Platforms — checked items build from top to bottom (Development)", FontWeight = FontWeight.SemiBold });
        var platformArea = new Grid { ColumnDefinitions = new ColumnDefinitions("*,220"), ColumnSpacing = 16 };
        platformScroll = new ScrollViewer { Content = rows, Height = 240, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var platformFrame = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = platformScroll };
        platformArea.Children.Add(platformFrame);
        var side = new StackPanel { Spacing = 10 }; Grid.SetColumn(side, 1);
        side.Children.Add(new TextBlock { Text = "Android texture format" }); side.Children.Add(flavor); inputs.Add(flavor);
        side.Children.Add(Button("Refresh platforms", Reload));
        var moves = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; moves.Children.Add(Button("Move up", () => Move(selectedIndex, selectedIndex - 1))); moves.Children.Add(Button("Move down", () => Move(selectedIndex, selectedIndex + 1))); side.Children.Add(moves);
        side.Children.Add(new TextBlock { Text = "Drag a platform name or use ↑ / ↓.\n\nHost: " + Host.Name, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        platformArea.Children.Add(side); platformGroup.Children.Add(platformArea); Grid.SetRow(platformGroup, 2); root.Children.Add(platformGroup);
        var output = PathField("Destination (default: project / Builds)", destination, () => BrowseFolder(destination), 3, Button("Use project Builds", DefaultDestination)); root.Children.Add(output);
        var hint = new TextBlock { Text = "Missing folders are created automatically. Save and close Unreal Editor before packaging.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray }; Grid.SetRow(hint, 4); root.Children.Add(hint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        actions.Children.Add(Button("Start packaging", () => Start(false))); actions.Children.Add(Button("Check setup", () => Start(true)));
        stop.Click += async (_, _) => await StopBuild(); actions.Children.Add(stop);
        var open = Button("Open destination", () => Host.OpenFolder(destination.Text ?? "")); inputs.Remove(open); actions.Children.Add(open);
        Grid.SetRow(actions, 5); root.Children.Add(actions); Grid.SetRow(status, 6); root.Children.Add(status);
        Grid.SetRow(logBox, 7); root.Children.Add(logBox); Content = root;
        project.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !loading) { DefaultDestination(); ClearStates(); } };
        engine.LostFocus += (_, _) => { if (!busy && engine.Text != loadedEngine) Safe(Reload); };
        flavor.SelectionChanged += (_, _) => { if (!loading && !busy && platforms.Count > 0) Safe(Save); };
        Closing += OnClosing;
        Closed += (_, _) => { logTimer.Stop(); dragTimer.Stop(); };
        logTimer.Tick += (_, _) => FlushLog(); logTimer.Start();
        dragTimer.Tick += (_, _) => AutoScrollDrag();
        LoadSettings();
    }
    Control PathField(string title, TextBox box, Func<Task> browse, int row, Control? extra = null)
    {
        var group = new StackPanel { Spacing = 5 }; group.Children.Add(new TextBlock { Text = title });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(extra == null ? "*,Auto" : "*,Auto,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(box); inputs.Add(box); var choose = Button("Browse…", browse); Grid.SetColumn(choose, 1); grid.Children.Add(choose);
        if (extra != null) { Grid.SetColumn(extra, 2); grid.Children.Add(extra); } group.Children.Add(grid); Grid.SetRow(group, row); return group;
    }
    Button Button(string caption, Action action) => Button(caption, () => { action(); return Task.CompletedTask; });
    Button Button(string caption, Func<Task> action)
    {
        var button = new Button { Content = caption, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += async (_, _) => { try { await action(); } catch (Exception e) { await Error(e.Message); } }; inputs.Add(button); return button;
    }
    async Task Error(string message) { status.Text = message; await Dialog("UEPackager", message, false); }
    async void Safe(Action action) { try { action(); } catch (Exception e) { await Error(e.Message); } }
    async Task<bool> Dialog(string title, string text, bool confirmation)
    {
        var dialog = new Window { Title = title, Width = 470, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 18 }; panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        if (confirmation) { var no = new Button { Content = "Keep running" }; no.Click += (_, _) => dialog.Close(false); actions.Children.Add(no); }
        var yes = new Button { Content = confirmation ? "Stop and close" : "OK" }; yes.Click += (_, _) => dialog.Close(true); actions.Children.Add(yes); panel.Children.Add(actions); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }
    async Task BrowseProject()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Unreal project", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Unreal project") { Patterns = ["*.uproject"] }] });
        var path = files.FirstOrDefault()?.TryGetLocalPath(); if (path == null) return; project.Text = path;
        engine.Text = Host.DetectEngine(path) ?? engine.Text; Reload(); Save();
    }
    async Task BrowseFolder(TextBox box)
    {
        var start = Directory.Exists(box.Text) ? await StorageProvider.TryGetFolderFromPathAsync(box.Text!) : null;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = box == engine ? "Select Unreal Engine folder" : "Select packaging destination", AllowMultiple = false, SuggestedStartLocation = start });
        var path = folders.FirstOrDefault()?.TryGetLocalPath(); if (path == null) return; box.Text = path; if (box == engine) Reload(); Save();
    }
    void DefaultDestination()
    {
        try { destination.Text = string.IsNullOrWhiteSpace(project.Text) ? "" : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(project.Text))!, "Builds"); }
        catch { destination.Text = ""; }
    }
    Settings Snapshot() => new() { Project = project.Text ?? "", EngineRoot = engine.Text ?? "", Destination = destination.Text ?? "", AndroidFlavor = flavor.SelectedItem as string ?? "ASTC", Platforms = platforms.Where(p => p.Checked).Select(p => p.Id).ToArray(), PlatformOrder = platforms.Select(p => p.Id).ToArray() };
    void Save() => store.Save(Snapshot());
    void LoadSettings()
    {
        try
        {
            loading = true; var settings = store.Load(); project.Text = settings.Project; engine.Text = settings.EngineRoot;
            destination.Text = settings.Destination; if (string.IsNullOrWhiteSpace(destination.Text)) DefaultDestination();
            flavor.SelectedItem = new[] { "ASTC", "ETC2", "DXT", "Multi" }.Contains(settings.AndroidFlavor) ? settings.AndroidFlavor : "ASTC";
            Reload(); var order = settings.PlatformOrder ?? []; platforms = platforms.OrderBy(p => { var i = Array.IndexOf(order, p.Id); return i < 0 ? int.MaxValue : i; }).ToList();
            foreach (var p in platforms) p.Checked = p.Enabled && (settings.Platforms ?? []).Contains(p.Id); RenderRows();
        }
        catch (Exception e) { status.Text = "Settings could not be loaded: " + e.Message; if (platforms.Count == 0) Reload(); }
        finally { loading = false; }
    }
    void Reload()
    {
        if (busy) return;
        var previous = platforms; var available = Catalog.Load(engine.Text ?? "");
        platforms = previous.Select(p => available.FirstOrDefault(a => a.Id == p.Id)).OfType<Platform>().Concat(available.Where(a => previous.All(p => p.Id != a.Id))).ToList();
        foreach (var p in platforms) { var old = previous.FirstOrDefault(a => a.Id == p.Id); p.Checked = p.Enabled && (old?.Checked ?? (p.Id == "Android" || p.Id == (Host.Name == "Windows" ? "Win64" : Host.Name == "Mac" ? "Mac" : "Linux"))); }
        loadedEngine = engine.Text ?? ""; selectedIndex = -1; RenderRows();
    }
    void ClearStates() { foreach (var p in platforms) { p.State = ""; UpdateState(p); } }
    void UpdateState(Platform p)
    {
        if (!stateLabels.TryGetValue(p.Id, out var label)) return;
        label.Text = p.State; label.Foreground = p.State == "Completed" ? Brushes.ForestGreen : p.State == "Failed" ? Brushes.Firebrick : p.State == "Running" ? Brushes.RoyalBlue : Brushes.DimGray;
    }
    void RenderRows()
    {
        rows.Children.Clear(); rowBorders.Clear(); stateLabels.Clear();
        for (int i = 0; i < platforms.Count; i++)
        {
            var index = i; var p = platforms[i];
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(6, 1) };
            var check = new CheckBox { IsChecked = p.Checked, IsEnabled = !busy && p.Enabled, VerticalAlignment = VerticalAlignment.Center, MinWidth = 22 };
            check.IsCheckedChanged += (_, _) => { p.Checked = check.IsChecked == true; if (!loading && !busy) Safe(Save); }; grid.Children.Add(check);
            var name = new TextBlock { Text = p.Label, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = p.Enabled ? Brushes.Black : Brushes.Gray, Cursor = new Cursor(StandardCursorType.SizeAll) };
            ToolTip.SetTip(name, p.Label + (!p.Enabled ? " — unavailable on " + Host.Name + " host" : !p.Detected ? " — verify platform components / SDK in Unreal" : ""));
            Grid.SetColumn(name, 1); grid.Children.Add(name);
            var state = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(state, 2); grid.Children.Add(state); stateLabels[p.Id] = state; UpdateState(p);
            var up = new Button { Content = "↑", Padding = new Thickness(9, 2), IsEnabled = !busy && index > 0 }; Grid.SetColumn(up, 3); grid.Children.Add(up); up.Click += (_, _) => Safe(() => Move(index, index - 1));
            var down = new Button { Content = "↓", Padding = new Thickness(9, 2), IsEnabled = !busy && index < platforms.Count - 1 }; Grid.SetColumn(down, 4); grid.Children.Add(down); down.Click += (_, _) => Safe(() => Move(index, index + 1));
            var border = new Border { Height = 34, Child = grid, BorderThickness = new Thickness(0, 2, 0, 0), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent }; rowBorders.Add(border); rows.Children.Add(border);
            name.PointerPressed += (_, e) => { if (busy || !e.GetCurrentPoint(name).Properties.IsLeftButtonPressed) return; pressedRow = index; selectedIndex = index; pressPoint = e.GetPosition(platformScroll); lastDragPoint = pressPoint; capturedPointer = e.Pointer; e.Pointer.Capture(name); HighlightSelection(); e.Handled = true; };
            name.PointerMoved += (_, e) => { if (pressedRow < 0 || capturedPointer != e.Pointer) return; lastDragPoint = e.GetPosition(platformScroll); if (!dragging && Math.Abs(lastDragPoint.X - pressPoint.X) + Math.Abs(lastDragPoint.Y - pressPoint.Y) < 5) return; dragging = true; UpdateDropPosition(); dragTimer.Start(); e.Handled = true; };
            name.PointerReleased += (_, e) => { if (pressedRow < 0) return; int source = pressedRow, target = dropGap > source ? dropGap - 1 : dropGap; bool move = dragging; EndDrag(); if (move && target >= 0) Safe(() => Move(source, target)); e.Handled = true; };
            name.PointerCaptureLost += (_, _) => EndDrag();
        }
        HighlightSelection();
    }
    void HighlightSelection() { for (int i = 0; i < rowBorders.Count; i++) rowBorders[i].Background = i == selectedIndex ? new SolidColorBrush(Color.Parse("#E7F2FA")) : Brushes.Transparent; }
    void UpdateDropPosition()
    {
        double y = lastDragPoint.Y + platformScroll.Offset.Y;
        int gap = Math.Clamp((int)Math.Floor((y + 17) / 34), 0, platforms.Count);
        if (gap == dropGap) return; dropGap = gap;
        for (int i = 0; i < rowBorders.Count; i++) { var border = rowBorders[i]; border.BorderThickness = new Thickness(0, 2, 0, i == platforms.Count - 1 && gap == platforms.Count ? 2 : 0); border.BorderBrush = i == gap || (i == platforms.Count - 1 && gap == platforms.Count) ? Brushes.DodgerBlue : Brushes.Transparent; }
    }
    void AutoScrollDrag()
    {
        if (!dragging) return;
        double offset = platformScroll.Offset.Y;
        if (lastDragPoint.Y < 24) offset -= 20; else if (lastDragPoint.Y > platformScroll.Viewport.Height - 24) offset += 20;
        platformScroll.Offset = new Vector(0, Math.Clamp(offset, 0, Math.Max(0, platformScroll.Extent.Height - platformScroll.Viewport.Height))); UpdateDropPosition();
    }
    void EndDrag()
    {
        pressedRow = -1; dragging = false; dropGap = -1; dragTimer.Stop(); var pointer = capturedPointer; capturedPointer = null; pointer?.Capture(null);
        foreach (var border in rowBorders) { border.BorderBrush = Brushes.Transparent; border.BorderThickness = new Thickness(0, 2, 0, 0); }
    }
    public void Move(int source, int target)
    {
        if (busy || source < 0 || target < 0 || source >= platforms.Count || target >= platforms.Count) return;
        Catalog.Move(platforms, source, target); selectedIndex = target; RenderRows(); Save();
    }
    void SetBusy(bool value)
    { busy = value; foreach (var input in inputs) input.IsEnabled = !value; stop.IsEnabled = value; RenderRows(); }
    void FlushLog()
    {
        var buffer = new StringBuilder(); int count = 0;
        while (count++ < 1000 && logQueue.TryDequeue(out var line)) buffer.AppendLine(line);
        if (buffer.Length == 0) return; var old = logBox.Text ?? ""; if (old.Length > 160000) old = old[^80000..]; logBox.Text = old + buffer; logBox.CaretIndex = logBox.Text.Length;
    }
    async Task Start(bool validate)
    {
        if (busy) return; EndDrag();
        if (string.IsNullOrWhiteSpace(destination.Text)) DefaultDestination();
        var settings = Snapshot();
        if (!File.Exists(settings.Project) || !settings.Project.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase)) throw new IOException("Select a valid .uproject file.");
        if (!Path.IsPathFullyQualified(settings.Destination)) throw new IOException("Select an absolute destination folder.");
        settings.Project = Path.GetFullPath(settings.Project); settings.EngineRoot = Path.GetFullPath(settings.EngineRoot); settings.Destination = Path.GetFullPath(settings.Destination);
        var selected = platforms.Where(p => p.Checked).ToList(); if (selected.Count == 0) throw new IOException("Select at least one platform."); Save();
        var logFolder = Path.Combine(settings.Destination, "Logs"); Directory.CreateDirectory(logFolder);
        var prefix = Path.Combine(logFolder, "Packaging-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(settings, SettingsStore.JsonOptions));
        using var writer = new StreamWriter(prefix + ".log", false, Encoding.UTF8) { AutoFlush = true }; var logGate = new object();
        logQueue.Clear(); logBox.Text = ""; runner = new BuildRunner(); var active = runner;
        foreach (var p in platforms) p.State = p.Checked ? (validate ? "Checking" : "Waiting") : "";
        SetBusy(true); status.Text = "Running — " + prefix + ".log";
        void Log(string line) { lock (logGate) writer.WriteLine(line); if (logQueue.Count < 10000) logQueue.Enqueue(line); }
        void State(Platform p, string state) => Dispatcher.UIThread.InvokeAsync(() => { p.State = state; UpdateState(p); }).GetAwaiter().GetResult();
        try { await Task.Run(() => active.Run(settings, selected, validate, Log, State)); status.Text = "Completed successfully. Logs: " + prefix + ".log"; }
        catch (Exception e) { Log(e.Message); status.Text = active.Stopped ? "Stopped. Output may be incomplete." : "Failed: " + e.Message; }
        finally
        {
            foreach (var p in selected) if (p.State is "Waiting" or "Checking" or "Running") p.State = active.Stopped ? "Stopped" : "Not built";
            FlushLog(); runner = null; SetBusy(false); if (closeWhenDone) { allowClose = true; Close(); }
        }
    }
    async Task StopBuild()
    {
        var active = runner; if (active == null) return; stop.IsEnabled = false;
        try { await Task.Run(active.Stop); } catch (Exception e) { closeWhenDone = false; stop.IsEnabled = true; await Error("Unable to stop: " + e.Message); }
    }
    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (allowClose) return;
        if (!busy) { Safe(Save); return; }
        e.Cancel = true;
        if (await Dialog("Packaging is running", "Stop the current build and close the tool?", true)) { closeWhenDone = true; await StopBuild(); if (!busy) { allowClose = true; Close(); } }
    }
}



