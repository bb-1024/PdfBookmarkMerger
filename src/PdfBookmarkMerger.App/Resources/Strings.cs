using System.Globalization;
using System.Resources;

namespace PdfBookmarkMerger.App.Resources;

/// <summary>
/// Strings.resx(既定=日本語、中立カルチャ)・Strings.en.resx(英語サテライト)への型安全なアクセサ。
/// Visual Studioのカスタムツール(ResXFileCodeGenerator)が生成するコードに相当するものを、
/// dotnet build単体でも確実に動作するよう手書きしている。
/// <see cref="Culture"/>を明示的に設定することで、OSのCurrentUICultureに関わらず
/// 表示言語を制御する(アプリ起動時にAppLanguageBootstrapperが設定する)。
/// </summary>
public static class Strings
{
    private static readonly ResourceManager ResourceManager =
        new("PdfBookmarkMerger.App.Resources.Strings", typeof(Strings).Assembly);

    public static CultureInfo? Culture { get; set; }

    private static string Get(string name) => ResourceManager.GetString(name, Culture) ?? name;

    public static string AppTitle => Get(nameof(AppTitle));
    public static string SelectFilesInstruction => Get(nameof(SelectFilesInstruction));
    public static string PageCountFormat => Get(nameof(PageCountFormat));
    public static string SettingsButton => Get(nameof(SettingsButton));
    public static string AddFilesButton => Get(nameof(AddFilesButton));
    public static string AddFolderButton => Get(nameof(AddFolderButton));
    public static string DeleteButton => Get(nameof(DeleteButton));
    public static string MoveUpButton => Get(nameof(MoveUpButton));
    public static string MoveDownButton => Get(nameof(MoveDownButton));
    public static string NextButton => Get(nameof(NextButton));
    public static string DragDropPlaceholder => Get(nameof(DragDropPlaceholder));

    public static string EditBookmarksInstructionLine1 => Get(nameof(EditBookmarksInstructionLine1));
    public static string EditBookmarksInstructionLine2 => Get(nameof(EditBookmarksInstructionLine2));
    public static string LevelFormat => Get(nameof(LevelFormat));
    public static string ExpandCheckboxLabel => Get(nameof(ExpandCheckboxLabel));
    public static string LeftLabel => Get(nameof(LeftLabel));
    public static string TopLabel => Get(nameof(TopLabel));
    public static string ZoomLabel => Get(nameof(ZoomLabel));
    public static string BookmarkOriginPrefixWithActionFormat => Get(nameof(BookmarkOriginPrefixWithActionFormat));
    public static string BookmarkOriginPrefixFormat => Get(nameof(BookmarkOriginPrefixFormat));
    public static string BookmarkOriginSuffix => Get(nameof(BookmarkOriginSuffix));
    public static string ResetPageNumberMenuItem => Get(nameof(ResetPageNumberMenuItem));
    public static string PromoteLevelButton => Get(nameof(PromoteLevelButton));
    public static string DemoteLevelButton => Get(nameof(DemoteLevelButton));
    public static string CollapseAllTreeButton => Get(nameof(CollapseAllTreeButton));
    public static string CollapseAllTreeButtonTooltip => Get(nameof(CollapseAllTreeButtonTooltip));
    public static string ExpandAllTreeButton => Get(nameof(ExpandAllTreeButton));
    public static string ExpandAllTreeButtonTooltip => Get(nameof(ExpandAllTreeButtonTooltip));
    public static string ExpandLevelLabel => Get(nameof(ExpandLevelLabel));
    public static string ExpandLevelTextBoxTooltip => Get(nameof(ExpandLevelTextBoxTooltip));
    public static string AddRootButton => Get(nameof(AddRootButton));
    public static string AddChildButton => Get(nameof(AddChildButton));
    public static string AddSiblingButton => Get(nameof(AddSiblingButton));
    public static string SetLevelCapButton => Get(nameof(SetLevelCapButton));
    public static string BackToFileListButton => Get(nameof(BackToFileListButton));
    public static string UndoButton => Get(nameof(UndoButton));
    public static string ForceFitCheckbox => Get(nameof(ForceFitCheckbox));
    public static string GlobalExpandCheckbox => Get(nameof(GlobalExpandCheckbox));
    public static string MergeButton => Get(nameof(MergeButton));
    public static string SaveBookmarkSettingsButton => Get(nameof(SaveBookmarkSettingsButton));
    public static string StatusBarProcessingLabel => Get(nameof(StatusBarProcessingLabel));
    public static string BusyProgressCountOnlyFormat => Get(nameof(BusyProgressCountOnlyFormat));
    public static string BusyProgressWithDetailFormat => Get(nameof(BusyProgressWithDetailFormat));

    public static string CancelButton => Get(nameof(CancelButton));
    public static string OkButton => Get(nameof(OkButton));

    public static string PropertiesDialogTitle => Get(nameof(PropertiesDialogTitle));
    public static string TitleFieldLabel => Get(nameof(TitleFieldLabel));
    public static string AuthorFieldLabel => Get(nameof(AuthorFieldLabel));
    public static string SubjectFieldLabel => Get(nameof(SubjectFieldLabel));
    public static string KeywordsFieldLabel => Get(nameof(KeywordsFieldLabel));
    public static string ApplicationFieldLabel => Get(nameof(ApplicationFieldLabel));

    public static string SettingsDialogTitle => Get(nameof(SettingsDialogTitle));
    public static string ThemeModeLabel => Get(nameof(ThemeModeLabel));
    public static string LanguageLabel => Get(nameof(LanguageLabel));
    public static string ShowPropertiesDialogCheckbox => Get(nameof(ShowPropertiesDialogCheckbox));
    public static string AppVersionFormat => Get(nameof(AppVersionFormat));
    public static string ThemeModeLight => Get(nameof(ThemeModeLight));
    public static string ThemeModeDark => Get(nameof(ThemeModeDark));
    public static string ThemeModeSystem => Get(nameof(ThemeModeSystem));
    public static string LanguageJapanese => Get(nameof(LanguageJapanese));
    public static string LanguageEnglish => Get(nameof(LanguageEnglish));

    public static string LevelCapDialogTitle => Get(nameof(LevelCapDialogTitle));
    public static string LevelCapDescription => Get(nameof(LevelCapDescription));
    public static string LevelCapLabel => Get(nameof(LevelCapLabel));

    public static string StatusReady => Get(nameof(StatusReady));
    public static string StatusLoading => Get(nameof(StatusLoading));
    public static string LoadErrorDialogTitle => Get(nameof(LoadErrorDialogTitle));
    public static string LoadErrorMessageFormat => Get(nameof(LoadErrorMessageFormat));
    public static string StatusNoLoadableFiles => Get(nameof(StatusNoLoadableFiles));
    public static string StatusLoadedAllSucceededFormat => Get(nameof(StatusLoadedAllSucceededFormat));
    public static string StatusLoadedWithFailuresFormat => Get(nameof(StatusLoadedWithFailuresFormat));
    public static string StatusUpdatingBookmarkTree => Get(nameof(StatusUpdatingBookmarkTree));
    public static string DefaultMergedFileName => Get(nameof(DefaultMergedFileName));
    public static string StatusMerging => Get(nameof(StatusMerging));
    public static string StatusMergeCompleteFormat => Get(nameof(StatusMergeCompleteFormat));
    public static string MergeCompleteDialogTitle => Get(nameof(MergeCompleteDialogTitle));
    public static string MergeCompleteMessageFormat => Get(nameof(MergeCompleteMessageFormat));
    public static string MergeErrorDialogTitle => Get(nameof(MergeErrorDialogTitle));
    public static string MergeErrorMessageFormat => Get(nameof(MergeErrorMessageFormat));
    public static string DefaultBookmarkSettingsFileName => Get(nameof(DefaultBookmarkSettingsFileName));
    public static string StatusSavingBookmarkSettings => Get(nameof(StatusSavingBookmarkSettings));
    public static string StatusSaveBookmarkSettingsCompleteFormat => Get(nameof(StatusSaveBookmarkSettingsCompleteFormat));
    public static string SaveBookmarkSettingsCompleteDialogTitle => Get(nameof(SaveBookmarkSettingsCompleteDialogTitle));
    public static string SaveBookmarkSettingsCompleteMessageFormat => Get(nameof(SaveBookmarkSettingsCompleteMessageFormat));
    public static string SaveBookmarkSettingsErrorDialogTitle => Get(nameof(SaveBookmarkSettingsErrorDialogTitle));
    public static string SaveBookmarkSettingsErrorMessageFormat => Get(nameof(SaveBookmarkSettingsErrorMessageFormat));

    public static string NewBookmarkDefaultTitle => Get(nameof(NewBookmarkDefaultTitle));
    public static string NoMergeTargetFilesError => Get(nameof(NoMergeTargetFilesError));

    public static string OpenPdfFilesDialogTitle => Get(nameof(OpenPdfFilesDialogTitle));
    public static string PdfFileFilterWpf => Get(nameof(PdfFileFilterWpf));
    public static string PdfFileTypeName => Get(nameof(PdfFileTypeName));
    public static string OpenFolderDialogTitle => Get(nameof(OpenFolderDialogTitle));
    public static string SaveMergedPdfDialogTitle => Get(nameof(SaveMergedPdfDialogTitle));
    public static string SaveBookmarkSettingsDialogTitle => Get(nameof(SaveBookmarkSettingsDialogTitle));
    public static string XmlFileFilterWpf => Get(nameof(XmlFileFilterWpf));
    public static string XmlFileTypeName => Get(nameof(XmlFileTypeName));
}
