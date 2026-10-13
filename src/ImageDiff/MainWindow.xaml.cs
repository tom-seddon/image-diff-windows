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

    // A left click that moves less than this (in screen units) flips instead of panning.
    private const double ClickSlop = 4;

    private PixelImage? _left, _right, _highlight;
    private DiffResult? _diff;
    private string _leftName = "", _rightName = "";
    private string? _pendingOutputPath;

    private ViewMode _mode = ViewMode.Difference;
    private bool _showingRight;
    private double _zoom = 1;
    private bool _fit = true;

    private int _loadVersion, _diffVersion;

    private bool _mouseDown;
    private bool _panning;
    private MouseButton _panButton;
    private Point _panStart;
    private Vector _panStartOffset;

    public MainWindow(CommandLineOptions options)
    {
        InitializeComponent();

        LeftPathBox.Text = options.LeftPath ?? "";
        RightPathBox.Text = options.RightPath ?? "";
        ToleranceSlider.Value = options.Tolerance;
        _pendingOutputPath = options.OutputPath;
        SetMode(options.Mode);

        ViewHost.SizeChanged += (_, _) => { if (_fit) ApplyFit(); };
        ViewHost.PreviewMouseWheel += ViewHost_PreviewMouseWheel;
        ViewHost.PreviewMouseMove += (_, e) => UpdatePixelReadout(e);
        ViewHost.MouseLeave += (_, _) => PixelText.Text = "";
        ViewCanvas.PreviewMouseDown += ViewCanvas_PreviewMouseDown;
        ViewCanvas.PreviewMouseMove += ViewCanvas_PreviewMouseMove;
        ViewCanvas.PreviewMouseUp += ViewCanvas_PreviewMouseUp;
        ViewCanvas.LostMouseCapture += (_, _) => EndMouse();

        Loaded += async (_, _) =>
        {
            ApplyZoom();
            if (HasBothPaths)
                await CompareAsync();
            else if (_pendingOutputPath != null)
                ShowError("--output needs two images to compare.");
        };
    }

    private bool HasBothPaths => !string.IsNullOrWhiteSpace(LeftPathBox.Text) && !string.IsNullOrWhiteSpace(RightPathBox.Text);

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
        _leftName = $"{Path.GetFileName(leftPath)}  ({left.Width}×{left.Height})";
        _rightName = $"{Path.GetFileName(rightPath)}  ({right.Width}×{right.Height})";

        LeftImage.Source = ImageFile.ToBitmapSource(left);
        RightImage.Source = ImageFile.ToBitmapSource(right);
        Title = $"{Path.GetFileName(leftPath)} vs {Path.GetFileName(rightPath)} - Image Diff";
        EmptyHint.Visibility = Visibility.Collapsed;
        FlipButton.IsEnabled = true;
        UpdateVisibleImage();

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
        ViewCanvas.Width = diff.Width;
        ViewCanvas.Height = diff.Height;
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
            try
            {
                string? dir = Path.GetDirectoryName(Path.GetFullPath(current));
                if (Directory.Exists(dir))
                    dialog.InitialDirectory = dir;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Not a usable path; open the dialog in its default folder.
            }
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
        if (ViewCanvas == null)
            return; // Still initialising.
        SetMode(sender == FlipModeButton ? ViewMode.Flip : ViewMode.Difference);
    }

    private void Flip_Click(object sender, RoutedEventArgs e) => Flip();

    private void SetMode(ViewMode mode)
    {
        _mode = mode;
        DiffModeButton.IsChecked = mode == ViewMode.Difference;
        FlipModeButton.IsChecked = mode == ViewMode.Flip;
        UpdateVisibleImage();
    }

    /// <summary>Switches between the left and right image, entering Flip view if needed.</summary>
    private void Flip()
    {
        if (_left == null || _right == null)
            return;
        if (_mode == ViewMode.Flip)
            _showingRight = !_showingRight;
        else
            SetMode(ViewMode.Flip);
        UpdateVisibleImage();
    }

    private void UpdateVisibleImage()
    {
        bool flip = _mode == ViewMode.Flip;
        // Switching is a plain visibility change: both images stay decoded and laid out, so it is instant.
        DiffImage.Visibility = flip ? Visibility.Collapsed : Visibility.Visible;
        LeftImage.Visibility = flip && !_showingRight ? Visibility.Visible : Visibility.Collapsed;
        RightImage.Visibility = flip && _showingRight ? Visibility.Visible : Visibility.Collapsed;

        bool loaded = _left != null && _right != null;
        FlipBadge.Visibility = flip && loaded ? Visibility.Visible : Visibility.Collapsed;
        FlipBadgeText.Text = _showingRight ? $"Right: {_rightName}" : $"Left: {_leftName}";
        FlipButton.Content = !flip ? "Flip" : _showingRight ? "Show left" : "Show right";
    }

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

        ViewScroll.UpdateLayout();
        double width = ViewScroll.ActualWidth - 2, height = ViewScroll.ActualHeight - 2;
        if (width <= 0 || height <= 0)
            return;

        _zoom = Math.Clamp(Math.Min(width / _diff.Width, height / _diff.Height), MinZoom, MaxZoom);
        ApplyZoom();
    }

    /// <summary>Sets the zoom, keeping the image point under <paramref name="anchor"/> (in viewport coordinates) still.</summary>
    private void SetZoom(double zoom, Point? anchor)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        var at = anchor ?? new Point(ViewScroll.ViewportWidth / 2, ViewScroll.ViewportHeight / 2);
        double imageX = (ViewScroll.HorizontalOffset + at.X) / _zoom;
        double imageY = (ViewScroll.VerticalOffset + at.Y) / _zoom;

        _zoom = zoom;
        ApplyZoom();

        ViewScroll.UpdateLayout();
        ViewScroll.ScrollToHorizontalOffset(imageX * _zoom - at.X);
        ViewScroll.ScrollToVerticalOffset(imageY * _zoom - at.Y);
    }

    private void ApplyZoom()
    {
        ViewScale.ScaleX = _zoom;
        ViewScale.ScaleY = _zoom;
        ZoomText.Text = $"{_zoom * 100:0.#}%";
    }

    private void ViewHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || _diff == null)
            return;
        e.Handled = true;
        _fit = false;
        double factor = e.Delta > 0 ? 1.25 : 0.8;
        SetZoom(_zoom * factor, e.GetPosition(ViewScroll));
    }

    // ---------------------------------------------------------------- Mouse: panning, click to flip, pixel readout

    private void ViewCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_mouseDown || e.ChangedButton is not (MouseButton.Left or MouseButton.Middle or MouseButton.Right))
            return;

        _mouseDown = true;
        _panning = false;
        _panButton = e.ChangedButton;
        _panStart = e.GetPosition(ViewScroll);
        _panStartOffset = new Vector(ViewScroll.HorizontalOffset, ViewScroll.VerticalOffset);
        ViewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void ViewCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown)
            return;

        var delta = e.GetPosition(ViewScroll) - _panStart;
        if (!_panning && delta.Length < ClickSlop)
            return;

        if (!_panning)
        {
            _panning = true;
            ViewCanvas.Cursor = Cursors.ScrollAll;
        }
        ViewScroll.ScrollToHorizontalOffset(_panStartOffset.X - delta.X);
        ViewScroll.ScrollToVerticalOffset(_panStartOffset.Y - delta.Y);
    }

    private void ViewCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown || e.ChangedButton != _panButton)
            return;

        // A left click without dragging flips between the images.
        bool flip = !_panning && _panButton == MouseButton.Left;
        ViewCanvas.ReleaseMouseCapture();
        EndMouse();
        e.Handled = true;

        if (flip)
            Flip();
    }

    private void EndMouse()
    {
        _mouseDown = false;
        _panning = false;
        ViewCanvas.ClearValue(CursorProperty);
    }

    private void UpdatePixelReadout(MouseEventArgs e)
    {
        if (_diff == null || _left == null || _right == null)
            return;

        var p = e.GetPosition(ViewCanvas);
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
            case Key.Space when !inTextBox:
                // Ignore auto-repeat so holding the key doesn't strobe between the images.
                e.Handled = true;
                if (!e.IsRepeat)
                    Flip();
                break;
            case Key.D1 or Key.NumPad1 when !inTextBox:
                e.Handled = true;
                SetMode(ViewMode.Difference);
                break;
            case Key.D2 or Key.NumPad2 when !inTextBox:
                e.Handled = true;
                SetMode(ViewMode.Flip);
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
