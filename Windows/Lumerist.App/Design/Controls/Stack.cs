using System.Windows;
using System.Windows.Controls;

namespace Lumerist.App.Design.Controls;

/// <summary>
/// A stack with a gap between children (SwiftUI's VStack/HStack spacing,
/// which WPF's StackPanel lacks). Collapsed children take no gap. Vertical
/// stacks give each child the full width, horizontal ones the full height,
/// each child's alignment deciding where it sits.
/// </summary>
internal sealed class Stack : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(Stack), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(Stack),
        new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    private bool IsVertical => Orientation == Orientation.Vertical;

    private IEnumerable<UIElement> Visible => InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed);

    protected override Size MeasureOverride(Size availableSize)
    {
        double along = 0, across = 0;
        var count = 0;
        var childAvailable = IsVertical
            ? new Size(availableSize.Width, double.PositiveInfinity)
            : new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Visible)
        {
            child.Measure(childAvailable);
            var size = child.DesiredSize;
            along += IsVertical ? size.Height : size.Width;
            across = Math.Max(across, IsVertical ? size.Width : size.Height);
            count++;
        }
        // Children not visible are still measured, so they keep a size when shown.
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) child.Measure(childAvailable);
        }
        if (count > 1) along += Spacing * (count - 1);
        return IsVertical ? new Size(across, along) : new Size(along, across);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double offset = 0;
        foreach (var child in Visible)
        {
            var size = child.DesiredSize;
            if (IsVertical)
            {
                child.Arrange(new Rect(0, offset, finalSize.Width, size.Height));
                offset += size.Height + Spacing;
            }
            else
            {
                child.Arrange(new Rect(offset, 0, size.Width, finalSize.Height));
                offset += size.Width + Spacing;
            }
        }
        return finalSize;
    }
}

/// <summary>
/// Gives its child the full width up to <see cref="MaximumWidth"/>, left
/// aligned (SwiftUI's <c>.frame(maxWidth: 1040, alignment: .leading)</c>).
/// A left-aligned element would shrink to its content instead, and the
/// spacers of the rows inside would collapse.
/// </summary>
internal sealed class LeadingColumn : Decorator
{
    public double MaximumWidth { get; set; } = double.PositiveInfinity;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return default;
        Child.Measure(new Size(Math.Min(constraint.Width, MaximumWidth), constraint.Height));
        return new Size(Math.Min(Child.DesiredSize.Width, MaximumWidth), Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(0, 0, Math.Min(arrangeSize.Width, MaximumWidth), arrangeSize.Height));
        return arrangeSize;
    }
}

/// <summary>Small layout helpers so code-built screens read like their SwiftUI originals.</summary>
internal static class Layout
{
    /// <summary>VStack(alignment: .leading, spacing:).</summary>
    public static Stack VStack(double spacing, params UIElement?[] children)
    {
        var stack = new Stack { Spacing = spacing };
        foreach (var child in children)
        {
            if (child is not null) stack.Children.Add(child);
        }
        return stack;
    }

    /// <summary>HStack(spacing:) with children centred vertically.</summary>
    public static Stack HStack(double spacing, params UIElement?[] children)
    {
        var stack = new Stack { Spacing = spacing, Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            if (child is null) continue;
            if (child is FrameworkElement element && element.ReadLocalValue(FrameworkElement.VerticalAlignmentProperty) == DependencyProperty.UnsetValue)
                element.VerticalAlignment = VerticalAlignment.Center;
            stack.Children.Add(child);
        }
        return stack;
    }

    /// <summary>
    /// An HStack with a Spacer: <paramref name="leading"/> on the left (the
    /// first one takes the free width when <paramref name="stretchFirst"/>),
    /// <paramref name="trailing"/> on the right, at least
    /// <paramref name="spacing"/> apart.
    /// </summary>
    public static Grid Bar(double spacing, IEnumerable<UIElement?> leading, IEnumerable<UIElement?> trailing, bool stretchFirst = false)
    {
        var grid = new Grid();
        var placed = 0;
        void Add(UIElement element, GridLength width)
        {
            var index = grid.ColumnDefinitions.Count;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            if (element is FrameworkElement framework)
            {
                if (framework.ReadLocalValue(FrameworkElement.VerticalAlignmentProperty) == DependencyProperty.UnsetValue)
                    framework.VerticalAlignment = VerticalAlignment.Center;
                if (placed > 0) framework.Margin = framework.Margin with { Left = framework.Margin.Left + spacing };
            }
            Grid.SetColumn(element, index);
            grid.Children.Add(element);
            placed++;
        }
        var leadingItems = leading.OfType<UIElement>().ToList();
        for (var index = 0; index < leadingItems.Count; index++)
            Add(leadingItems[index], index == 0 && stretchFirst ? new GridLength(1, GridUnitType.Star) : GridLength.Auto);
        if (!stretchFirst || leadingItems.Count == 0)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var element in trailing.OfType<UIElement>()) Add(element, GridLength.Auto);
        return grid;
    }
}
