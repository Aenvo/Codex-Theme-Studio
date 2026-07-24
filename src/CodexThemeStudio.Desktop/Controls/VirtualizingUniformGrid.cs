using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CodexThemeStudio.Desktop.Controls;

public sealed class VirtualizingUniformGrid : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty MinimumItemWidthProperty = DependencyProperty.Register(
        nameof(MinimumItemWidth),
        typeof(double),
        typeof(VirtualizingUniformGrid),
        new FrameworkPropertyMetadata(280d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(double),
        typeof(VirtualizingUniformGrid),
        new FrameworkPropertyMetadata(316d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(VirtualizingUniformGrid),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(VirtualizingUniformGrid),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size extent;
    private Size viewport;
    private Point offset;
    private int columns = 1;
    private double itemWidth;

    public double MinimumItemWidth
    {
        get => (double)GetValue(MinimumItemWidthProperty);
        set => SetValue(MinimumItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var itemCount = owner?.Items.Count ?? 0;
        var width = double.IsInfinity(availableSize.Width) ? MinimumItemWidth : Math.Max(0, availableSize.Width);
        var height = double.IsInfinity(availableSize.Height) ? ItemHeight : Math.Max(0, availableSize.Height);

        columns = Math.Max(1, (int)Math.Floor((width + HorizontalSpacing) /
            (Math.Max(1, MinimumItemWidth) + HorizontalSpacing)));
        itemWidth = columns == 0
            ? width
            : Math.Max(0, (width - (columns - 1) * HorizontalSpacing) / columns);

        var rowStride = Math.Max(1, ItemHeight + VerticalSpacing);
        var rowCount = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)columns);
        var extentHeight = rowCount == 0 ? 0 : rowCount * rowStride - VerticalSpacing;
        UpdateScrollInfo(new Size(width, extentHeight), new Size(width, height));

        var firstRow = Math.Max(0, (int)Math.Floor(VerticalOffset / rowStride));
        var visibleRows = Math.Max(1, (int)Math.Ceiling(ViewportHeight / rowStride) + 1);
        var firstIndex = Math.Min(itemCount, firstRow * columns);
        var lastIndex = Math.Min(itemCount - 1, (firstRow + visibleRows) * columns - 1);

        CleanUpItems(firstIndex, lastIndex);
        if (itemCount > 0 && firstIndex <= lastIndex)
        {
            RealizeItems(firstIndex, lastIndex);
        }

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(itemWidth, ItemHeight));
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        var rowStride = ItemHeight + VerticalSpacing;
        var columnStride = itemWidth + HorizontalSpacing;

        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0)
            {
                continue;
            }

            var row = itemIndex / columns;
            var column = itemIndex % columns;
            InternalChildren[childIndex].Arrange(new Rect(
                column * columnStride,
                row * rowStride - VerticalOffset,
                itemWidth,
                ItemHeight));
        }

        return finalSize;
    }

    private void RealizeItems(int firstIndex, int lastIndex)
    {
        var generator = ItemContainerGenerator;
        var startPosition = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
        {
            for (var itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var newlyRealized);
                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }
            }
        }
    }

    private void CleanUpItems(int firstIndex, int lastIndex)
    {
        var generator = ItemContainerGenerator;
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex >= firstIndex && itemIndex <= lastIndex)
            {
                continue;
            }

            generator.Remove(position, 1);
            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private void UpdateScrollInfo(Size newExtent, Size newViewport)
    {
        var changed = newExtent != extent || newViewport != viewport;
        extent = newExtent;
        viewport = newViewport;
        SetVerticalOffset(VerticalOffset);
        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; }

    public double ExtentWidth => extent.Width;

    public double ExtentHeight => extent.Height;

    public double ViewportWidth => viewport.Width;

    public double ViewportHeight => viewport.Height;

    public double HorizontalOffset => offset.X;

    public double VerticalOffset => offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(VerticalOffset - 36);

    public void LineDown() => SetVerticalOffset(VerticalOffset + 36);

    public void LineLeft() { }

    public void LineRight() { }

    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 96);

    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 96);

    public void MouseWheelLeft() { }

    public void MouseWheelRight() { }

    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);

    public void PageLeft() { }

    public void PageRight() { }

    public void SetHorizontalOffset(double value) { }

    public void SetVerticalOffset(double value)
    {
        var newOffset = Math.Max(0, Math.Min(value, Math.Max(0, ExtentHeight - ViewportHeight)));
        if (Math.Abs(newOffset - offset.Y) < 0.1)
        {
            return;
        }

        offset.Y = newOffset;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = visual as DependencyObject;
        while (child is not null && VisualTreeHelper.GetParent(child) != this)
        {
            child = VisualTreeHelper.GetParent(child);
        }

        if (child is not UIElement container)
        {
            return rectangle;
        }

        var owner = ItemsControl.GetItemsOwner(this);
        var itemIndex = owner?.ItemContainerGenerator.IndexFromContainer(container) ?? -1;
        if (itemIndex < 0)
        {
            return rectangle;
        }

        var top = itemIndex / columns * (ItemHeight + VerticalSpacing);
        var bottom = top + ItemHeight;
        if (top < VerticalOffset)
        {
            SetVerticalOffset(top);
        }
        else if (bottom > VerticalOffset + ViewportHeight)
        {
            SetVerticalOffset(bottom - ViewportHeight);
        }

        return new Rect(0, top, itemWidth, ItemHeight);
    }
}
