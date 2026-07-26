using PdfSharp.Fonts;

namespace PdfBookmarkMerger.SampleGenerator;

/// <summary>
/// コンソールアプリではPDFsharpのGDIベース既定フォント解決が働かないため、
/// Windows同梱のArialフォントファイルを直接返す最小限のリゾルバー。
/// </summary>
internal sealed class ArialFontResolver : IFontResolver
{
    private static readonly string FontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(FontsDirectory, faceName switch
    {
        "arial-bold" => "arialbd.ttf",
        "arial-italic" => "ariali.ttf",
        "arial-bolditalic" => "arialbi.ttf",
        _ => "arial.ttf",
    }));

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => (isBold, isItalic) switch
    {
        (true, true) => new FontResolverInfo("arial-bolditalic"),
        (true, false) => new FontResolverInfo("arial-bold"),
        (false, true) => new FontResolverInfo("arial-italic"),
        _ => new FontResolverInfo("arial"),
    };
}
