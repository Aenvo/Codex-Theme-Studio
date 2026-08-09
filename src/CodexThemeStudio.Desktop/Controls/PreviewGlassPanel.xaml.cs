using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace CodexThemeStudio.Desktop.Controls;

[ContentProperty(nameof(PanelContent))]
public partial class PreviewGlassPanel : UserControl
{
    private const double ReferencePreviewWidth = 1240;
    private const double ReferencePreviewHeight = 780;
    private Rect lastSampleRect = Rect.Empty;
    private Size lastSourceSize = Size.Empty;
    private double lastEffectiveBlur = double.NaN;

    public static readonly DependencyProperty BackdropSourceProperty =
        DependencyProperty.Register(
            nameof(BackdropSource),
            typeof(FrameworkElement),
            typeof(PreviewGlassPanel),
            new PropertyMetadata(null, OnBackdropSourceChanged));

    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(
            nameof(BlurRadius),
            typeof(double),
            typeof(PreviewGlassPanel),
            new PropertyMetadata(0d, OnVisualPropertyChanged));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(PreviewGlassPanel),
            new PropertyMetadata(default(CornerRadius), OnVisualPropertyChanged));

    public static readonly DependencyProperty PanelContentProperty =
        DependencyProperty.Register(
            nameof(PanelContent),
            typeof(object),
            typeof(PreviewGlassPanel),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SurfaceOpacityProperty =
        DependencyProperty.Register(
            nameof(SurfaceOpacity),
            typeof(double),
            typeof(PreviewGlassPanel),
            new PropertyMetadata(1d));

    public PreviewGlassPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    public FrameworkElement? BackdropSource
    {
        get => (FrameworkElement?)GetValue(BackdropSourceProperty);
        set => SetValue(BackdropSourceProperty, value);
    }

    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public object? PanelContent
    {
        get => GetValue(PanelContentProperty);
        set => SetValue(PanelContentProperty, value);
    }

    public double SurfaceOpacity
    {
        get => (double)GetValue(SurfaceOpacityProperty);
        set => SetValue(SurfaceOpacityProperty, value);
    }

    internal Rect CurrentSampleRect => lastSampleRect;

    internal double EffectiveBlurRadius => double.IsNaN(lastEffectiveBlur)
        ? 0
        : lastEffectiveBlur;

    private static void OnBackdropSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var panel = (PreviewGlassPanel)dependencyObject;
        if (panel.IsLoaded)
        {
            panel.Unsubscribe(args.OldValue as FrameworkElement);
            panel.Subscribe(args.NewValue as FrameworkElement);
        }

        panel.InvalidateBackdrop();
    }

    private static void OnVisualPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((PreviewGlassPanel)dependencyObject).InvalidateBackdrop();

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Subscribe(BackdropSource);
        UpdateBackdrop();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) =>
        Unsubscribe(BackdropSource);

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) =>
        InvalidateBackdrop();

    private void OnSourceLayoutUpdated(object? sender, EventArgs args) =>
        UpdateBackdrop();

    private void Subscribe(FrameworkElement? source)
    {
        if (source is not null)
        {
            source.LayoutUpdated += OnSourceLayoutUpdated;
        }
    }

    private void Unsubscribe(FrameworkElement? source)
    {
        if (source is not null)
        {
            source.LayoutUpdated -= OnSourceLayoutUpdated;
        }
    }

    private void InvalidateBackdrop()
    {
        lastSampleRect = Rect.Empty;
        lastSourceSize = Size.Empty;
        lastEffectiveBlur = double.NaN;
        if (IsLoaded)
        {
            UpdateBackdrop();
        }
    }

    private void UpdateBackdrop()
    {
        var source = BackdropSource;
        if (source is null ||
            source.ActualWidth <= 0 ||
            source.ActualHeight <= 0 ||
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            BackdropRectangle.Fill = Brushes.Transparent;
            return;
        }

        Point origin;
        try
        {
            origin = TranslatePoint(new Point(0, 0), source);
        }
        catch (InvalidOperationException)
        {
            BackdropRectangle.Fill = Brushes.Transparent;
            return;
        }

        var sourceSize = new Size(source.ActualWidth, source.ActualHeight);
        var sampleRect = new Rect(origin, new Size(ActualWidth, ActualHeight));
        var previewScale = Math.Min(
            source.ActualWidth / ReferencePreviewWidth,
            source.ActualHeight / ReferencePreviewHeight);
        var effectiveBlur = Math.Max(0, BlurRadius) * previewScale;
        if (sampleRect == lastSampleRect &&
            sourceSize == lastSourceSize &&
            Math.Abs(effectiveBlur - lastEffectiveBlur) < 0.001)
        {
            return;
        }

        var blurPadding = Math.Ceiling(effectiveBlur);
        var expandedSample = sampleRect;
        expandedSample.Inflate(blurPadding, blurPadding);

        Canvas.SetLeft(BackdropRectangle, -blurPadding);
        Canvas.SetTop(BackdropRectangle, -blurPadding);
        BackdropRectangle.Width = ActualWidth + (blurPadding * 2);
        BackdropRectangle.Height = ActualHeight + (blurPadding * 2);
        BackdropRectangle.Fill = new VisualBrush(source)
        {
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            Stretch = Stretch.Fill,
            Viewbox = expandedSample,
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        BackdropBlur.Radius = effectiveBlur;
        ClipRoot.Clip = CreateRoundedClip(RenderSize, CornerRadius);

        lastSampleRect = sampleRect;
        lastSourceSize = sourceSize;
        lastEffectiveBlur = effectiveBlur;
    }

    private static Geometry CreateRoundedClip(Size size, CornerRadius radius)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return Geometry.Empty;
        }

        var cornerRadius = Math.Max(
            0,
            Math.Min(
                Math.Min(radius.TopLeft, radius.TopRight),
                Math.Min(radius.BottomRight, radius.BottomLeft)));
        return new RectangleGeometry(
            new Rect(new Point(0, 0), size),
            cornerRadius,
            cornerRadius);
    }
}
