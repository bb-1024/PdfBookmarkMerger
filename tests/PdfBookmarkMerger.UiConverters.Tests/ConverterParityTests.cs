using System.Globalization;
using PdfBookmarkMerger.App.Options;
using Shouldly;
using WpfConverters = PdfBookmarkMerger.WpfApp.Converters;
using AvaloniaConverters = PdfBookmarkMerger.AvaloniaApp.Converters;

namespace PdfBookmarkMerger.UiConverters.Tests;

/// <summary>
/// PdfBookmarkMerger.Wpf と PdfBookmarkMerger.Avalonia は、UIフレームワーク(WPF-UI/Avalonia)の違いにより
/// Converter類を独立して実装している(design.htmlに明記された意図的な構成)。この二重実装は
/// 「片方だけ修正して挙動が食い違う」事故が起きやすい一方、それを検知する自動テストが無かった。
/// ここでは両実装に同じ入力を与え、変換結果が一致することをゴールデンテストとして固定する。
/// 乖離が起きた場合はこのテストが失敗し、意図した変更か・修正漏れかをその場で判別できる。
/// </summary>
public sealed class ConverterParityTests
{
    private static readonly WpfConverters.ZoomPercentToStringConverter WpfZoom = new();
    private static readonly AvaloniaConverters.ZoomPercentToStringConverter AvaloniaZoom = new();

    private static readonly WpfConverters.NullableDoubleToStringConverter WpfNullableDouble = new();
    private static readonly AvaloniaConverters.NullableDoubleToStringConverter AvaloniaNullableDouble = new();

    private static readonly WpfConverters.ThemeModeToLabelConverter WpfThemeLabel = new();
    private static readonly AvaloniaConverters.ThemeModeToLabelConverter AvaloniaThemeLabel = new();

    private static readonly WpfConverters.DepthToTitleWidthConverter WpfDepthWidth = new();
    private static readonly AvaloniaConverters.DepthToTitleWidthConverter AvaloniaDepthWidth = new();

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(0.3333)]
    [InlineData(10.0)]
    public void ZoomPercentToStringConverter_Convert_MatchesBetweenWpfAndAvalonia(double zoomRatio)
    {
        var wpfResult = WpfZoom.Convert(zoomRatio, typeof(string), null, CultureInfo.InvariantCulture);
        var avaloniaResult = AvaloniaZoom.Convert(zoomRatio, typeof(string), null, CultureInfo.InvariantCulture);

        wpfResult.ShouldBe(avaloniaResult);
    }

    [Theory]
    [InlineData("100")]
    [InlineData("33.3")]
    [InlineData("0")]
    [InlineData("")]
    public void ZoomPercentToStringConverter_ConvertBack_MatchesBetweenWpfAndAvalonia(string text)
    {
        var wpfResult = WpfZoom.ConvertBack(text, typeof(double?), null, CultureInfo.InvariantCulture);
        var avaloniaResult = AvaloniaZoom.ConvertBack(text, typeof(double?), null, CultureInfo.InvariantCulture);

        wpfResult.ShouldBe(avaloniaResult);
    }

    [Fact]
    public void ZoomPercentToStringConverter_ConvertBack_InvalidText_BothReturnTheirOwnDoNothingSentinel()
    {
        var wpfResult = WpfZoom.ConvertBack("not-a-number", typeof(double?), null, CultureInfo.InvariantCulture);
        var avaloniaResult = AvaloniaZoom.ConvertBack("not-a-number", typeof(double?), null, CultureInfo.InvariantCulture);

        // DoNothingの実体はフレームワークごとの別オブジェクトのため一致しないが、
        // 両方とも「値を確定しない(＝バインディング解除・入力を無視)」という同じ意味のセンチネルを返すこと。
        ReferenceEquals(wpfResult, System.Windows.Data.Binding.DoNothing).ShouldBeTrue();
        ReferenceEquals(avaloniaResult, Avalonia.Data.BindingOperations.DoNothing).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(12.5)]
    [InlineData(-3.333)]
    public void NullableDoubleToStringConverter_Convert_MatchesBetweenWpfAndAvalonia(double value)
    {
        var wpfResult = WpfNullableDouble.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
        var avaloniaResult = AvaloniaNullableDouble.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

        wpfResult.ShouldBe(avaloniaResult);
    }

    [Fact]
    public void NullableDoubleToStringConverter_Convert_NonDoubleValue_MatchesBetweenWpfAndAvalonia()
    {
        var wpfResult = WpfNullableDouble.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);
        var avaloniaResult = AvaloniaNullableDouble.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        wpfResult.ShouldBe(avaloniaResult);
        wpfResult.ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    [InlineData(ThemeMode.System)]
    public void ThemeModeToLabelConverter_Convert_MatchesBetweenWpfAndAvalonia(ThemeMode mode)
    {
        var wpfResult = WpfThemeLabel.Convert(mode, typeof(string), null, CultureInfo.InvariantCulture);
        var avaloniaResult = AvaloniaThemeLabel.Convert(mode, typeof(string), null, CultureInfo.InvariantCulture);

        wpfResult.ShouldBe(avaloniaResult);
    }

    // DepthToTitleWidthConverterは「1階層あたりの実際のインデント幅」をTreeViewの階層分だけ
    // タイトル列の幅から差し引くことで、以降の列(表示方法・座標欄等)の縦位置を階層に関わらず揃える。
    // この「1階層あたりの実際のインデント幅」はWPF-UI(19px)とAvalonia FluentTheme(16px、
    // ヘッドレステストで実測して判明)のTreeViewItemで異なるため、Convert結果はWPF/Avaloniaで
    // 一致しないのが正しい(むしろ一致していたら、どちらかの定数が実際のテーマと食い違っている)。
    // そのため、他のConverterと違いWPF/Avalonia間の一致ではなく、各実装が「自分自身のIndentPerLevel
    // 定数」に基づいた式の通りに計算しているかをそれぞれ固定する。
    [Theory]
    [InlineData(0, 220.0, 220.0)]
    [InlineData(1, 220.0, 201.0)]
    [InlineData(3, 220.0, 163.0)]
    [InlineData(2, 50.0, 70.0)]
    public void DepthToTitleWidthConverter_Wpf_UsesOwnIndentPerLevelOf19(int depth, double baseWidth, double expected)
    {
        var wpfResult = WpfDepthWidth.Convert([depth, baseWidth], typeof(double), null, CultureInfo.InvariantCulture);

        wpfResult.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 220.0, 220.0)]
    [InlineData(1, 220.0, 204.0)]
    [InlineData(3, 220.0, 172.0)]
    [InlineData(2, 50.0, 70.0)]
    public void DepthToTitleWidthConverter_Avalonia_UsesOwnIndentPerLevelOf16(int depth, double baseWidth, double expected)
    {
        var avaloniaResult = AvaloniaDepthWidth.Convert([depth, baseWidth], typeof(double), null, CultureInfo.InvariantCulture);

        avaloniaResult.ShouldBe(expected);
    }
}
