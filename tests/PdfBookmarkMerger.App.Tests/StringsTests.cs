using System.Globalization;
using PdfBookmarkMerger.App.Resources;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// Strings(Resources/Strings.resx・Strings.en.resx)の言語切り替えが正しく機能することを検証する。
/// dotnet build単体ではVisual Studioのリソースコード自動生成が働かないため、
/// Strings.csを手書きしている。この配線が壊れていないかを確認する回帰テスト。
/// </summary>
public sealed class StringsTests
{
    public StringsTests() => Strings.Culture = null;

    [Fact]
    public void Culture_Null_ReturnsJapaneseNeutralResource()
    {
        Strings.Culture = null;

        Strings.SettingsButton.ShouldBe("設定...");
        Strings.OkButton.ShouldBe("OK");
    }

    [Fact]
    public void Culture_English_ReturnsEnglishSatelliteResource()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");

        Strings.SettingsButton.ShouldBe("Settings...");
        Strings.MergeButton.ShouldBe("Merge and Continue to Link Editing...");
    }

    [Fact]
    public void Culture_Japanese_ReturnsJapaneseResourceEvenWhenExplicitlySet()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("ja");

        Strings.SettingsButton.ShouldBe("設定...");
    }

    [Fact]
    public void EveryStringProperty_HasBothJapaneseAndEnglishTranslations()
    {
        var properties = typeof(Strings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .ToList();

        properties.ShouldNotBeEmpty();

        foreach (var property in properties)
        {
            Strings.Culture = CultureInfo.GetCultureInfo("ja");
            var ja = (string)property.GetValue(null)!;

            Strings.Culture = CultureInfo.GetCultureInfo("en");
            var en = (string)property.GetValue(null)!;

            ja.ShouldNotBe(property.Name, $"{property.Name} はja翻訳が未設定です(キー名がそのまま返っています)。");
            en.ShouldNotBe(property.Name, $"{property.Name} はen翻訳が未設定です(キー名がそのまま返っています)。");
        }
    }
}
