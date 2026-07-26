using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PdfBookmarkMerger.WpfApp.Controls;

/// <summary>
/// しおりツリーのD&amp;D中に、ドロップした場合の挿入位置を横線で示すアドナー。
/// Adobe Acrobatのしおりパネルのように、対象行の上半分/下半分どちらにカーソルがあるかで
/// 「その行の子として挿入」「その行と並列(兄弟)として挿入」を視覚的に区別する。
/// </summary>
public sealed class BookmarkInsertionAdorner : Adorner
{
    private const double DotRadius = 3.5;

    private static readonly Pen LinePen = CreatePen();

    private double _x;
    private double _y;
    private double _width;

    public BookmarkInsertionAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    public void UpdatePosition(double x, double y, double width)
    {
        _x = x;
        _y = y;
        _width = Math.Max(0, width);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawEllipse(Brushes.DodgerBlue, null, new Point(_x, _y), DotRadius, DotRadius);
        drawingContext.DrawLine(LinePen, new Point(_x, _y), new Point(_x + _width, _y));
    }

    private static Pen CreatePen()
    {
        var pen = new Pen(Brushes.DodgerBlue, 2);
        pen.Freeze();
        return pen;
    }
}
