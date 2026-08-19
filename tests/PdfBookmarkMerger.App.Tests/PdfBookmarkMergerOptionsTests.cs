using System.Reflection;
using PdfBookmarkMerger.App.Options;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// PdfBookmarkMergerOptions.Clone()の回帰テスト。MainWindowViewModel.MergeCoreAsync・
/// SettingsViewModel.ToOptionsが、かつて設定の一部フィールドを手動で列挙して複製しており、
/// 新しいプロパティ(例: ShowMergeAndEditLinksButton)の追加時に書き漏れて既定値へ黙って
/// 戻ってしまう不具合が実際に発生した。Clone()へ一本化した後も、将来同種のプロパティが
/// 追加された際に単体テストが検知できるよう、リフレクションで全パブリックプロパティの値が
/// 複製されていることを検証する。
/// </summary>
public sealed class PdfBookmarkMergerOptionsTests
{
    [Fact]
    public void Clone_CopiesEveryPublicProperty()
    {
        var source = new PdfBookmarkMergerOptions
        {
            LastOutputDirectory = @"C:\output",
            WindowWidth = 1234,
            WindowHeight = 567,
            ThemeMode = ThemeMode.Dark,
            ShowPropertiesDialogOnMerge = true,
            ShowMergeAndEditLinksButton = true,
            Language = AppLanguage.English,
        };

        var clone = source.Clone();

        clone.ShouldNotBeSameAs(source);

        foreach (var property in typeof(PdfBookmarkMergerOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead)
            {
                continue;
            }

            var sourceValue = property.GetValue(source);
            var cloneValue = property.GetValue(clone);
            cloneValue.ShouldBe(sourceValue, $"プロパティ '{property.Name}' がClone()でコピーされていません。");
        }
    }

    [Fact]
    public void Clone_ProducesAnIndependentCopy_MutatingTheCloneDoesNotAffectTheSource()
    {
        var source = new PdfBookmarkMergerOptions { LastOutputDirectory = @"C:\before" };

        var clone = source.Clone();
        clone.LastOutputDirectory = @"C:\after";

        source.LastOutputDirectory.ShouldBe(@"C:\before");
    }
}
