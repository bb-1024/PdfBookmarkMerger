using System.Globalization;
using System.Windows.Data;
using PdfBookmarkMerger.App.ViewModels;

namespace PdfBookmarkMerger.WpfApp.Converters;

/// <summary>
/// しおりツリーの階層の深さ(Depth)と、ツリー全体で共有するタイトル列の基準幅(BaseWidth、
/// 最も長いタイトルに合わせてUI側が実測・更新する)から、各行のタイトル欄の幅を求める。
/// TreeViewの階層インデント分を差し引くことで、ComboBox以降の列が階層に関わらず縦に揃って見えるようにする。
/// </summary>
public sealed class DepthToTitleWidthConverter : IMultiValueConverter
{
    private const double IndentPerLevel = 19;
    private const double MinWidth = 70;

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = values.Length > 0 && values[0] is int d ? d : 0;
        var baseWidth = values.Length > 1 && values[1] is double b ? b : BookmarkTreeViewModel.DefaultTitleColumnBaseWidth;
        return Math.Max(MinWidth, baseWidth - (depth * IndentPerLevel));
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
