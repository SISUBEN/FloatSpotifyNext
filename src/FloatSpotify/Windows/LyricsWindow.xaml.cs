using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FloatSpotify.Storage;
using FloatSpotify.ViewModels;
using Forms = System.Windows.Forms;

namespace FloatSpotify.Windows;

public partial class LyricsWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const long ToolWindowStyle = 0x00000080L;
    private const long TransparentStyle = 0x00000020L;
    private const long NoActivateStyle = 0x08000000L;

    private readonly OverlayViewModel _viewModel;
    private bool _allowClose;
    private bool _pointerDown;
    private bool _isDragging;
    private System.Windows.Point _dragStartScreen;
    private double _dragStartLeft;
    private double _dragStartTop;

    public LyricsWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

#if DEBUG
        ShowInTaskbar = true;
        ShowActivated = true;
#endif

        SourceInitialized += (_, _) => ApplyWindowStyles();
        ContentRendered += (_, _) => RestorePosition();
        Closing += OnClosing;
        SizeChanged += (_, e) => OnLyricsSizeChanged(e);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.PlacementRequested += ApplyPlacement;
    }

    public void ShowOverlay()
    {
        if (!IsVisible)
            Show();

        Topmost = _viewModel.AlwaysOnTop;
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void LyricSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !CanStartDrag(e.OriginalSource as DependencyObject, _viewModel.IsLocked))
            return;

        _pointerDown = true;
        _isDragging = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        if (!LyricSurface.CaptureMouse())
        {
            _pointerDown = false;
            return;
        }
        e.Handled = true;
    }

    private void LyricSurface_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pointerDown || e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentScreen = PointToScreen(e.GetPosition(this));
        var pixelDelta = currentScreen - _dragStartScreen;
        var source = PresentationSource.FromVisual(this);
        var dipDelta = source?.CompositionTarget?.TransformFromDevice.Transform(pixelDelta)
                       ?? pixelDelta;

        if (!_isDragging && dipDelta.Length > 4)
            _isDragging = true;

        if (!_isDragging) return;

        Left = _dragStartLeft + dipDelta.X;
        Top = _dragStartTop + dipDelta.Y;
        e.Handled = true;
    }

    private void LyricSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown || e.ChangedButton != MouseButton.Left)
            return;

        _pointerDown = false;
        var wasDragging = _isDragging;
        LyricSurface.ReleaseMouseCapture();

        if (wasDragging)
            ClampPositionToVirtualScreen();
        else
            _viewModel.RequestToggleControls();

        _isDragging = false;
        e.Handled = true;
    }

    internal static bool CanStartDrag(DependencyObject? origin, bool locked)
    {
        if (locked) return false;
        for (var node = origin; node is not null;)
        {
            // Text/plain-lyrics surfaces remain draggable; scrollbar gestures stay native.
            if (node is System.Windows.Controls.Primitives.ScrollBar or Thumb or System.Windows.Controls.Primitives.ButtonBase) return false;
            node = node is Visual ? VisualTreeHelper.GetParent(node)
                : (node as FrameworkContentElement)?.Parent;
        }
        return true;
    }

    private void LyricSurface_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _pointerDown = false;
        _isDragging = false;
    }

    private void RestorePosition()
    {
        if (_viewModel.OverlayPlacement != OverlayPlacement.Custom)
        {
            ApplyPlacement(_viewModel.OverlayPlacement);
            return;
        }

        RestoreCustomPosition();
    }

    private void RestoreCustomPosition()
    {
        if (_viewModel.SavedLeft is double savedLeft &&
            _viewModel.SavedTop is double savedTop)
        {
            var maximumLeft = Math.Max(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
            var maximumTop = Math.Max(
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight);
            Left = Math.Clamp(
                savedLeft,
                SystemParameters.VirtualScreenLeft,
                maximumLeft);
            Top = Math.Clamp(
                savedTop,
                SystemParameters.VirtualScreenTop,
                maximumTop);
            return;
        }

        Left = SystemParameters.WorkArea.Left +
               (SystemParameters.WorkArea.Width - ActualWidth) / 2;
        Top = SystemParameters.WorkArea.Top +
              SystemParameters.WorkArea.Height * 0.72;
        _viewModel.SavePosition(Left, Top);
    }

    private void ApplyPlacement(OverlayPlacement placement)
    {
        if (placement == OverlayPlacement.Custom)
        {
            RestoreCustomPosition();
            return;
        }

        var workArea = CurrentMonitorWorkArea();
        const double margin = 24;
        var centeredLeft = workArea.Left + (workArea.Width - ActualWidth) / 2;
        var centeredTop = workArea.Top + (workArea.Height - ActualHeight) / 2;
        var right = workArea.Right - ActualWidth - margin;
        var bottom = workArea.Bottom - ActualHeight - margin;

        (Left, Top) = placement switch
        {
            OverlayPlacement.Top => (centeredLeft, workArea.Top + margin),
            OverlayPlacement.Center => (centeredLeft, centeredTop),
            OverlayPlacement.Bottom => (centeredLeft, bottom),
            OverlayPlacement.TopLeft => (workArea.Left + margin, workArea.Top + margin),
            OverlayPlacement.TopRight => (right, workArea.Top + margin),
            OverlayPlacement.BottomLeft => (workArea.Left + margin, bottom),
            OverlayPlacement.BottomRight => (right, bottom),
            _ => (Left, Top)
        };

        ClampPositionToVirtualScreen(false);
    }

    private Rect CurrentMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var workingArea = Forms.Screen.FromHandle(handle).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice;
        if (transform is null)
        {
            return new Rect(
                workingArea.Left,
                workingArea.Top,
                workingArea.Width,
                workingArea.Height);
        }

        var topLeft = transform.Value.Transform(
            new System.Windows.Point(workingArea.Left, workingArea.Top));
        var bottomRight = transform.Value.Transform(
            new System.Windows.Point(workingArea.Right, workingArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void ClampPositionToVirtualScreen(bool savePosition = true)
    {
        var maximumLeft = Math.Max(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
        var maximumTop = Math.Max(
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight);
        Left = Math.Clamp(
            Left,
            SystemParameters.VirtualScreenLeft,
            maximumLeft);
        Top = Math.Clamp(
            Top,
            SystemParameters.VirtualScreenTop,
            maximumTop);
        if (savePosition)
            _viewModel.SavePosition(Left, Top);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.Track))
            LyricScroller.ScrollToTop();
        if (e.PropertyName == nameof(OverlayViewModel.IsLocked))
            ApplyWindowStyles();
    }

    private void OnLyricsSizeChanged(SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || e.PreviousSize.Width <= 0) return;

        if (_viewModel.OverlayPlacement == OverlayPlacement.Custom)
        {
            Left -= (e.NewSize.Width - e.PreviousSize.Width) / 2;
            ClampPositionToVirtualScreen(false);
        }
        else
        {
            ApplyPlacement(_viewModel.OverlayPlacement);
        }
    }

    private void ApplyWindowStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var style = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();

#if DEBUG
        style &= ~(ToolWindowStyle | NoActivateStyle);
#else
        style |= ToolWindowStyle | NoActivateStyle;
#endif

        if (_viewModel.IsLocked)
            style |= TransparentStyle;
        else
            style &= ~TransparentStyle;

        SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(style));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;

        e.Cancel = true;
        Hide();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newLong);
}
