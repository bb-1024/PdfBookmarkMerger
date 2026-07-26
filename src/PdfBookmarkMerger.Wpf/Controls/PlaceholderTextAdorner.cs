using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PdfBookmarkMerger.WpfApp.Controls;

/// <summary>
/// 空のListBox等の中央にヒントテキストを重ねて表示するアドナー。AdornerLayerに乗るだけで
/// 元のコントロールの可視ツリー構造(D&amp;Dのヒットテスト対象を含む)を一切変更しないため、
/// ドラッグ&amp;ドロップ等の挙動に影響を与えない。
/// </summary>
public sealed class PlaceholderTextAdorner : Adorner
{
    private readonly FormattedText _formattedText;

    public PlaceholderTextAdorner(UIElement adornedElement, string text, Brush foreground) : base(adornedElement)
    {
        IsHitTestVisible = false;

        _formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            14,
            foreground,
            VisualTreeHelper.GetDpi(adornedElement).PixelsPerDip);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = AdornedElement.RenderSize;
        var origin = new Point(
            (bounds.Width - _formattedText.Width) / 2,
            (bounds.Height - _formattedText.Height) / 2);
        drawingContext.DrawText(_formattedText, origin);
    }
}
