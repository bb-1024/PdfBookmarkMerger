using System.Globalization;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.ViewModels;
using Shouldly;
using WpfConverters = PdfBookmarkMerger.WpfApp.Converters;
using AvaloniaConverters = PdfBookmarkMerger.AvaloniaApp.Converters;

namespace PdfBookmarkMerger.UiConverters.Tests;

/// <summary>
/// PdfBookmarkMerger.Wpf と PdfBookmarkMerger.Avalonia は、UIフレームワーク(WPF-UI/Avalonia)の違いにより
/// Converter類を独立して実装している(docs/ja/04-ui-design.htmlに明記された意図的な構成)。この二重実装は
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

    private static readonly WpfConverters.PageNumberWidthConverter WpfPageNumberWidth = new();
    private static readonly AvaloniaConverters.PageNumberWidthConverter AvaloniaPageNumberWidth = new();

    private static readonly WpfConverters.EditedHighlightBrushConverter WpfEditedHighlight = new();
    private static readonly AvaloniaConverters.EditedHighlightBrushConverter AvaloniaEditedHighlight = new();

    private static readonly WpfConverters.ByteArrayToImageConverter WpfByteArrayToImage = new();
    private static readonly AvaloniaConverters.ByteArrayToImageConverter AvaloniaByteArrayToImage = new();

    private static readonly WpfConverters.LinkGroupDisplayConverter WpfLinkGroupDisplay = new();
    private static readonly AvaloniaConverters.LinkGroupDisplayConverter AvaloniaLinkGroupDisplay = new();

    // 1x1の透明PNG(最小の有効なPNG)。
    private static readonly byte[] OnePixelPng =
        System.Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

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

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(9999)]
    [InlineData(-7)]
    [InlineData(0)]
    public void PageNumberWidthConverter_Convert_MatchesBetweenWpfAndAvalonia(int pageNumber)
    {
        var wpfResult = WpfPageNumberWidth.Convert(pageNumber, typeof(double), null, CultureInfo.InvariantCulture);
        var avaloniaResult = AvaloniaPageNumberWidth.Convert(pageNumber, typeof(double), null, CultureInfo.InvariantCulture);

        wpfResult.ShouldBe(avaloniaResult);
    }

    [Fact]
    public void PageNumberWidthConverter_Convert_LargerNumberOfDigits_ProducesLargerWidth()
    {
        var narrow = (double)WpfPageNumberWidth.Convert(5, typeof(double), null, CultureInfo.InvariantCulture);
        var wide = (double)WpfPageNumberWidth.Convert(123456, typeof(double), null, CultureInfo.InvariantCulture);

        wide.ShouldBeGreaterThan(narrow);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EditedHighlightBrushConverter_Convert_MatchesBetweenWpfAndAvalonia(bool isEdited)
    {
        // Avalonia.Media.Brushes.Transparentは(可変な)SolidColorBrushではなくImmutableSolidColorBrushを
        // 返すため、両方の実装が持つISolidColorBrush(Colorプロパティのみ)経由で比較する。
        var wpfBrush = (System.Windows.Media.SolidColorBrush)WpfEditedHighlight.Convert(isEdited, typeof(object), null, CultureInfo.InvariantCulture);
        var avaloniaBrush = (Avalonia.Media.ISolidColorBrush)AvaloniaEditedHighlight.Convert(isEdited, typeof(object), null, CultureInfo.InvariantCulture);

        wpfBrush.Color.A.ShouldBe(avaloniaBrush.Color.A);
        wpfBrush.Color.R.ShouldBe(avaloniaBrush.Color.R);
        wpfBrush.Color.G.ShouldBe(avaloniaBrush.Color.G);
        wpfBrush.Color.B.ShouldBe(avaloniaBrush.Color.B);
    }

    [Fact]
    public void ByteArrayToImageConverter_Convert_NullOrEmpty_BothReturnNull()
    {
        WpfByteArrayToImage.Convert(null, typeof(object), null, CultureInfo.InvariantCulture).ShouldBeNull();
        AvaloniaByteArrayToImage.Convert(null, typeof(object), null, CultureInfo.InvariantCulture).ShouldBeNull();

        WpfByteArrayToImage.Convert(Array.Empty<byte>(), typeof(object), null, CultureInfo.InvariantCulture).ShouldBeNull();
        AvaloniaByteArrayToImage.Convert(Array.Empty<byte>(), typeof(object), null, CultureInfo.InvariantCulture).ShouldBeNull();
    }

    [Fact]
    public void ByteArrayToImageConverter_Convert_ValidPng_BothProduceAnImageOfMatchingPixelSize()
    {
        var wpfImage = (System.Windows.Media.Imaging.BitmapImage)WpfByteArrayToImage.Convert(
            OnePixelPng, typeof(object), null, CultureInfo.InvariantCulture)!;
        var avaloniaImage = (Avalonia.Media.Imaging.Bitmap)AvaloniaByteArrayToImage.Convert(
            OnePixelPng, typeof(object), null, CultureInfo.InvariantCulture)!;

        wpfImage.PixelWidth.ShouldBe(avaloniaImage.PixelSize.Width);
        wpfImage.PixelHeight.ShouldBe(avaloniaImage.PixelSize.Height);
        wpfImage.PixelWidth.ShouldBe(1);
    }

    [Fact]
    public void LinkGroupDisplayConverter_Convert_MatchesBetweenWpfAndAvalonia_AndUsesOneBasedPageNumbers()
    {
        var group = new LinkGroupInfo(Guid.NewGuid(), SourcePageIndex: 2, TargetPageIndex: 9, RectCount: 1, IsPreExisting: false);

        var wpfResult = WpfLinkGroupDisplay.Convert(group, typeof(string), null, CultureInfo.InvariantCulture);
        var avaloniaResult = AvaloniaLinkGroupDisplay.Convert(group, typeof(string), null, CultureInfo.InvariantCulture);

        wpfResult.ShouldBe(avaloniaResult);
        wpfResult.ShouldBe(string.Format(App.Resources.Strings.LinkGroupDisplayFormat, 3, 10));
    }
}
