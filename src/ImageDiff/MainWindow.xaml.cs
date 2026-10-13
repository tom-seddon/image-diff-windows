using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageDiff.Core;
using Microsoft.Win32;

namespace ImageDiff;

public partial class MainWindow : Window
{
    private const double MinZoom = 0.02, MaxZoom = 64;

    private PixelImage? _left, _right, _highlight;
    private DiffResult? _diff;
    private string? _pendingOutputPath;

    private ViewMode _mode = ViewMode.Difference;
    private double _zoom = 1;
    private bool _fit = true;
    private double _split = 0.5;

    private int _loadVersion, _diffVersion;
    private bool _syncingScroll;

    private ScrollViewer? _panScroll;
    private Point _panStart;
    private Vector _panStartOffset;
    private bool _draggingSplit;

    public MainWindow(CommandLineOptions options)
    {
        InitializeComponent();

        LeftPathBox.Text = options.LeftPath ?? "";
        RightPathBox.Text = options.RightPath ?? "";
        ToleranceSlider.Value = options.Tolerance;
        _pendingOutputPath = options.OutputPath;
        SetMode(options.Mode);

        foreach (var scroll in AllScrollViewers)
            scroll.ScrollChanged += Scroll_ScrollChanged;

        ViewHost.SizeChanged += (_, _) => { if (_fit) ApplyFit(); };
        ViewHost.PreviewMouseWheel += ViewHost_PreviewMouseWheel;
        ViewHost.PreviewMouseDown += ViewHost_PreviewMouseDown;
        ViewHost.PreviewMouseMove += ViewHost_PreviewMouseMove;
        ViewHost.PreviewMouseUp += ViewHost_PreviewMouseUp;
        ViewHost.MouseLeave += (_, _) => PixelText.Text = "";
        ViewHost.LostMouseCapture += (_, _) => { _panScroll = null; ViewHost.ClearValue(CursorProperty); };

        SliderCanvas.MouseLeftButtonDown += SliderCanvas_MouseLeftButtonDown;
        SliderCanvas.MouseMove += SliderCanvas_MouseMove;
        SliderCanvas.MouseLeftButtonUp += (_, _) => { _draggingSplit = false; SliderCanvas.ReleaseMouseCapture(); };
        SliderCanvas.LostMouseCapture += (_, _) => _draggingSplit = false;

        Loaded += async (_, _) =>
        {
            ApplyZoom();
            if (HasBothPaths)
                await CompareAsync();
            else if (_pendingOutputPath != null)
                ShowError("--output needs two images to compare.");
        };
    }

    private ScrollViewer[] AllScrollViewers => [DiffScroll, SideLeftScroll, SideRightScroll, SliderScroll];

    private bool HasBothPaths => !string.IsNullOrWhiteSpace(LeftPathBox.Text) && !string.IsNullOrWhiteSpace(RightPathBox.Text);

    /// <summary>The scroll viewer that drives zoom anchoring and fitting in the current view.</summary>
    private ScrollViewer ActiveScroll => _mode switch
    {
        ViewMode.SideBySide => SideLeftScroll,
        ViewMode.Slider => SliderScroll,
        _ => DiffScroll,
    };

    // ---------------------------------------------------------------- Loading and comparing

    private async Task CompareAsync()
    {
        string leftPath = Unquote(LeftPathBox.Text), rightPath = Unquote(RightPathBox.Text);
        if (leftPath.Length == 0 || rightPath.Length == 0)
        {
            ShowError("Choose both a left and a right image.");
            return;
        }

        int version = ++_loadVersion;
        ShowStatus("Loading…");

        PixelImage left, right;
        try
        {
            (left, right) = await Task.Run(() => (ImageFile.Load(leftPath), ImageFile.Load(rightPath)));
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
                ShowError($"Could not load images: {ex.Message}");
            return;
        }
        if (version != _loadVersion)
            return;

        _left = left;
        _right = right;

        SideLeftImage.Source = SliderLeftImage.Source = ImageFile.ToBitmapSource(left);
        SideRightImage.Source = SliderRightImage.Source = ImageFile.ToBitmapSource(right);
        SideLeftHeader.Text = $"{Path.GetFileName(leftPath)}  ({left.Width}×{left.Height})";
        SideRightHeader.Text = $"{Path.GetFileName(rightPath)}  ({right.Width}×{right.Height})";
        SideLeftHeader.ToolTip = leftPath;
        SideRightHeader.ToolTip = rightPath;
        Title = $"{Path.GetFileName(leftPath)} vs {Path.GetFileName(rightPath)} - Image Diff";
        EmptyHint.Visibility = Visibility.Collapsed;

        await RecomputeDiffAsync();
    }

    private async Task RecomputeDiffAsync()
    {
        if (_left == null || _right == null)
            return;

        var left = _left;
        var right = _right;
        int tolerance = (int)ToleranceSlider.Value;
        int version = ++_diffVersion;

        var (diff, highlight) = await Task.Run(() =>
        {
            var d = ImageComparer.Compare(left, right, tolerance);
            return (d, ImageComparer.RenderHighlight(d, left));
        });
        if (version != _diffVersion || left != _left || right != _right)
            return;

        bool sizeChanged = _diff == null || _diff.Width != diff.Width || _diff.Height != diff.Height;
        _diff = diff;
        _highlight = highlight;
        DiffImage.Source = ImageFile.ToBitmapSource(highlight);
        SaveDiffButton.IsEnabled = true;

        foreach (var canvas in new[] { DiffCanvas, SideLeftCanvas, SideRightCanvas, SliderCanvas })
        {
            canvas.Width = diff.Width;
            canvas.Height = diff.Height;
        }
        UpdateSlider();
        ShowStatus(diff.Summary);

        if (sizeChanged && _fit)
            ApplyFit();

        if (_pendingOutputPath != null)
        {
            string output = _pendingOutputPath;
            _pendingOutputPath = null;
            SaveHighlight(output);
        }
    }

    private void SaveHighlight(string path)
    {
        if (_highlight == null)
            return;
        try
        {
            ImageFile.SavePng(_highlight, path);
            ShowStatus($"{_diff?.Summary} Difference image saved to {path}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            ShowError($"Could not save the difference image: {ex.Message}");
        }
    }

    private void ShowStatus(string text)
    {
        SummaryText.Text = text;
        SummaryText.ClearValue(TextBlock.ForegroundProperty);
    }

    private void ShowError(string text)
    {
        SummaryText.Text = text;
        SummaryText.Foreground = Brushes.Firebrick;
    }

    private static string Unquote(string path) => path.Trim().Trim('"');

    // ---------------------------------------------------------------- Toolbar

    private async void Compare_Click(object sender, RoutedEventArgs e) => await CompareAsync();

    private async void Swap_Click(object sender, RoutedEventArgs e)
    {
        (LeftPathBox.Text, RightPathBox.Text) = (RightPathBox.Text, LeftPathBox.Text);
        if (HasBothPaths)
            await CompareAsync();
    }

    private async void BrowseLeft_Click(object sender, RoutedEventArgs e) => await BrowseAsync(LeftPathBox, "Choose the left image");

    private async void BrowseRight_Click(object sender, RoutedEventArgs e) => await BrowseAsync(RightPathBox, "Choose the right image");

    private async Task BrowseAsync(TextBox target, string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = ImageFile.OpenFilter };
        string current = Unquote(target.Text);
        if (current.Length > 0)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(current));
            if (Directory.Exists(dir))
                dialog.InitialDirectory = dir;
        }
        if (dialog.ShowDialog(this) != true)
            return;

        target.Text = dialog.FileName;
        if (HasBothPaths)
            await CompareAsync();
    }

    private async void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await CompareAsync();
        }
    }

    private void SaveDiff_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save difference image",
            Filter = "PNG image|*.png",
            DefaultExt = ".png",
            FileName = Path.GetFileNameWithoutExtension(Unquote(LeftPathBox.Text)) + "-diff.png",
        };
        if (dialog.ShowDialog(this) == true)
            SaveHighlight(dialog.FileName);
    }

    private async void Tolerance_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ToleranceText == null)
            return; // Still initialising.
        ToleranceText.Text = ((int)e.NewValue).ToString();
        await RecomputeDiffAsync();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (DiffScroll == null)
            return; // Still initialising.
        var mode = sender == SideModeButton ? ViewMode.SideBySide
            : sender == SliderModeButton ? ViewMode.Slider
            : ViewMode.Difference;
        SetMode(mode);
    }

    private void SetMode(ViewMode mode)
    {
        var previous = ActiveScroll;
        _mode = mode;

        DiffModeButton.IsChecked = mode == ViewMode.Difference;
        SideModeButton.IsChecked = mode == ViewMode.SideBySide;
        SliderModeButton.IsChecked = mode == ViewMode.Slider;

        DiffScroll.Visibility = mode == ViewMode.Difference ? Visibility.Visible : Visibility.Collapsed;
        SideView.Visibility = mode == ViewMode.SideBySide ? Visibility.Visible : Visibility.Collapsed;
        SliderScroll.Visibility = mode == ViewMode.Slider ? Visibility.Visible : Visibility.Collapsed;

        // Keep the same part of the image in view when switching.
        var next = ActiveScroll;
        if (next != previous && IsLoaded)
        {
            double h = previous.HorizontalOffset, v = previous.VerticalOffset;
            Dispatcher.InvokeAsync(() =>
            {
                if (_fit)
                {
                    ApplyFit();
                    return;
                }
                foreach (var scroll in VisibleScrollViewers())
                {
                    scroll.ScrollToHorizontalOffset(h);
                    scroll.ScrollToVerticalOffset(v);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private IEnumerable<ScrollViewer> VisibleScrollViewers() => _mode switch
    {
        ViewMode.SideBySide => [SideLeftScroll, SideRightScroll],
        ViewMode.Slider => [SliderScroll],
        _ => [DiffScroll],
    };

    // ---------------------------------------------------------------- Zoom and scrolling

    private void ZoomFit_Click(object sender, RoutedEventArgs e) => ApplyFit();

    private void ZoomActual_Click(object sender, RoutedEventArgs e)
    {
        _fit = false;
        SetZoom(1, null);
    }

    private void ApplyFit()
    {
        _fit = true;
        if (_diff == null || _diff.Width == 0 || _diff.Height == 0)
            return;

        var scroll = ActiveScroll;
        scroll.UpdateLayout();
        double width = scroll.ActualWidth - 2, height = scroll.ActualHeight - 2;
        if (width <= 0 || height <= 0)
            return;

        _zoom = Math.Clamp(Math.Min(width / _diff.Width, height / _diff.Height), MinZoom, MaxZoom);
        ApplyZoom();
    }

    /// <summary>Sets the zoom, keeping the image point under <paramref name="anchor"/> (in viewport coordinates) still.</summary>
    private void SetZoom(double zoom, Point? anchor)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        var scroll = ActiveScroll;
        var at = anchor ?? new Point(scroll.ViewportWidth / 2, scroll.ViewportHeight / 2);
        double imageX = (scroll.HorizontalOffset + at.X) / _zoom;
        double imageY = (scroll.VerticalOffset + at.Y) / _zoom;

        _zoom = zoom;
        ApplyZoom();

        scroll.UpdateLayout();
        scroll.ScrollToHorizontalOffset(imageX * _zoom - at.X);
        scroll.ScrollToVerticalOffset(imageY * _zoom - at.Y);
    }

    private void ApplyZoom()
    {
        foreach (var scale in new[] { DiffScale, SideLeftScale, SideRightScale, SliderScale })
        {
            scale.ScaleX = _zoom;
            scale.ScaleY = _zoom;
        }
        ZoomText.Text = $"{_zoom * 100:0.#}%";
        UpdateSlider();
    }

    private void ViewHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || _diff == null)
            return;
        e.Handled = true;
        _fit = false;
        double factor = e.Delta > 0 ? 1.25 : 0.8;
        SetZoom(_zoom * factor, e.GetPosition(ActiveScroll));
    }

    private void Scroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Side-by-side panes scroll together.
        if (_syncingScroll || _mode != ViewMode.SideBySide || (e.HorizontalChange == 0 && e.VerticalChange == 0))
            return;
        var other = sender == SideLeftScroll ? SideRightScroll : sender == SideRightScroll ? SideLeftScroll : null;
        if (other == null)
            return;
        _syncingScroll = true;
        other.ScrollToHorizontalOffset(e.HorizontalOffset);
        other.ScrollToVerticalOffset(e.VerticalOffset);
        _syncingScroll = false;
    }

    // ---------------------------------------------------------------- Mouse: panning, slider, pixel readout

    private void ViewHost_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        bool panButton = e.ChangedButton is MouseButton.Middle or MouseButton.Right
            || (e.ChangedButton == MouseButton.Left && _mode != ViewMode.Slider);
        if (!panButton)
            return;

        var canvas = CanvasUnder(e.OriginalSource as DependencyObject);
        if (canvas == null)
            return;

        _panScroll = FindAncestor<ScrollViewer>(canvas);
        if (_panScroll == null)
            return;
        _panStart = e.GetPosition(ViewHost);
        _panStartOffset = new Vector(_panScroll.HorizontalOffset, _panScroll.VerticalOffset);
        ViewHost.CaptureMouse();
        ViewHost.Cursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void ViewHost_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_panScroll != null)
        {
            var delta = e.GetPosition(ViewHost) - _panStart;
            _panScroll.ScrollToHorizontalOffset(_panStartOffset.X - delta.X);
            _panScroll.ScrollToVerticalOffset(_panStartOffset.Y - delta.Y);
        }
        UpdatePixelReadout(e);
    }

    private void ViewHost_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panScroll == null)
            return;
        _panScroll = null;
        ViewHost.ReleaseMouseCapture();
        ViewHost.ClearValue(CursorProperty);
        e.Handled = true;
    }

    private void SliderCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingSplit = SliderCanvas.CaptureMouse();
        MoveSplit(e.GetPosition(SliderCanvas).X);
        e.Handled = true;
    }

    private void SliderCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingSplit)
            MoveSplit(e.GetPosition(SliderCanvas).X);
    }

    private void MoveSplit(double x)
    {
        if (_diff == null || _diff.Width == 0)
            return;
        _split = Math.Clamp(x / _diff.Width, 0, 1);
        UpdateSlider();
    }

    private void UpdateSlider()
    {
        if (_diff == null)
            return;
        double x = _split * _diff.Width;
        SliderLeftImage.Clip = new RectangleGeometry(new Rect(0, 0, x, _diff.Height));
        // Keep the divider about two screen pixels wide at any zoom.
        double lineWidth = 2 / _zoom;
        SliderDivider.Width = lineWidth;
        SliderDivider.Margin = new Thickness(x - lineWidth / 2, 0, 0, 0);
    }

    private void UpdatePixelReadout(MouseEventArgs e)
    {
        if (_diff == null || _left == null || _right == null)
            return;

        FrameworkElement? canvas = _mode switch
        {
            ViewMode.SideBySide => SideRightCanvas.IsMouseOver ? SideRightCanvas : SideLeftCanvas,
            ViewMode.Slider => SliderCanvas,
            _ => DiffCanvas,
        };
        var p = e.GetPosition(canvas);
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        if (x < 0 || y < 0 || x >= _diff.Width || y >= _diff.Height)
        {
            PixelText.Text = "";
            return;
        }

        string marker = _diff.Mask[y * _diff.Width + x] ? "≠" : "=";
        PixelText.Text = $"({x}, {y})  L {PixelHex(_left, x, y)} {marker} R {PixelHex(_right, x, y)}";
    }

    private static string PixelHex(PixelImage image, int x, int y)
    {
        if (x >= image.Width || y >= image.Height)
            return "  (none)  ";
        int i = (y * image.Width + x) * 4;
        byte[] p = image.Bgra;
        return $"#{p[i + 3]:X2}{p[i + 2]:X2}{p[i + 1]:X2}{p[i]:X2}";
    }

    private FrameworkElement? CanvasUnder(DependencyObject? element)
    {
        for (var e = element; e != null; e = e is Visual ? VisualTreeHelper.GetParent(e) : LogicalTreeHelper.GetParent(e))
        {
            if (e == DiffCanvas || e == SideLeftCanvas || e == SideRightCanvas || e == SliderCanvas)
                return (FrameworkElement)e;
            if (e is ScrollViewer)
                return null;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var e = VisualTreeHelper.GetParent(element); e != null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is T match)
                return match;
        }
        return null;
    }

    // ---------------------------------------------------------------- Keyboard

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool inTextBox = Keyboard.FocusedElement is TextBox;

        switch (e.Key)
        {
            case Key.F5:
                e.Handled = true;
                await CompareAsync();
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                e.Handled = true;
                ApplyFit();
                break;
            case Key.D1 or Key.NumPad1 when ctrl:
                e.Handled = true;
                _fit = false;
                SetZoom(1, null);
                break;
            case Key.D1 or Key.NumPad1 when !inTextBox:
                e.Handled = true;
                SetMode(ViewMode.Difference);
                break;
            case Key.D2 or Key.NumPad2 when !inTextBox:
                e.Handled = true;
                SetMode(ViewMode.SideBySide);
                break;
            case Key.D3 or Key.NumPad3 when !inTextBox:
                e.Handled = true;
                SetMode(ViewMode.Slider);
                break;
        }
    }

    // ---------------------------------------------------------------- Drag and drop

    private static string[]? DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFiles(e) is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        var files = DroppedFiles(e);
        if (files is not { Length: > 0 })
            return;

        if (files.Length >= 2)
        {
            LeftPathBox.Text = files[0];
            RightPathBox.Text = files[1];
        }
        else if (string.IsNullOrWhiteSpace(LeftPathBox.Text))
        {
            LeftPathBox.Text = files[0];
        }
        else if (string.IsNullOrWhiteSpace(RightPathBox.Text))
        {
            RightPathBox.Text = files[0];
        }
        else
        {
            // Both already set: dropping on the left half replaces the left image, otherwise the right.
            bool leftHalf = e.GetPosition(ViewHost).X < ViewHost.ActualWidth / 2;
            (leftHalf ? LeftPathBox : RightPathBox).Text = files[0];
        }

        e.Handled = true;
        if (HasBothPaths)
            await CompareAsync();
    }

    private void PathBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFiles(e) is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void PathBox_PreviewDrop(object sender, DragEventArgs e)
    {
        var files = DroppedFiles(e);
        if (files is not { Length: > 0 })
            return;

        var box = (TextBox)sender;
        box.Text = files[0];
        if (files.Length >= 2)
            (box == LeftPathBox ? RightPathBox : LeftPathBox).Text = files[1];

        e.Handled = true;
        if (HasBothPaths)
            await CompareAsync();
    }
}
