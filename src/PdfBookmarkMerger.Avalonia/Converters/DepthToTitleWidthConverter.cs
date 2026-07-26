using System.Globalization;
using Avalonia.Data.Converters;
using PdfBookmarkMerger.App.ViewModels;

namespace PdfBookmarkMerger.AvaloniaApp.Converters;

/// <summary>
/// しおりツリーの階層の深さ(Depth)と、ツリー全体で共有するタイトル列の基準幅(BaseWidth、
/// 最も長いタイトルに合わせてUI側が実測・更新する)から、各行のタイトル欄の幅を求める。
/// TreeViewの階層インデント分を差し引くことで、ComboBox以降の列が階層に関わらず縦に揃って見えるようにする。
/// </summary>
public sealed class DepthToTitleWidthConverter : IMultiValueConverter
{
    // AvaloniaのFluentTheme既定TreeViewItemが1階層あたりに適用する実際のインデント幅(px)。
    // WPF版(WPF-UI既定TreeViewItem)の19pxとは異なり、ヘッドレステストで実測すると16pxだったため、
    // ここをWPFと同じ値のままにしていると、階層が深いノードほど後続列が徐々に右へずれてしまう。
    private const double IndentPerLevel = 16;
    private const double MinWidth = 70;

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = values.Count > 0 && values[0] is int d ? d : 0;
        var baseWidth = values.Count > 1 && values[1] is double b ? b : BookmarkTreeViewModel.DefaultTitleColumnBaseWidth;
        return Math.Max(MinWidth, baseWidth - (depth * IndentPerLevel));
    }
}
