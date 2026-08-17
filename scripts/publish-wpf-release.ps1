<#
.SYNOPSIS
    PdfBookmarkMerger (WPF-UI版) のリリース用publishビルドを実行し、
    dist/ に「展開済みフォルダ」と「そのzipアーカイブ」の両方を生成する。

.DESCRIPTION
    実行内容:
      1. dotnet publish (Release, 自己完結型・単一ファイル・圧縮・PDB除外) を
         dist/PdfBookmarkMerger-Wpf-v<Version>-<RID>/ へ直接出力する
      2. そのフォルダの内容を dist/PdfBookmarkMerger-Wpf-v<Version>-<RID>.zip に圧縮する

    dist/ は.gitignore対象(git管理外)のため、フォルダ・zipのどちらも削除せずに残す
    (フォルダはそのまま実行して動作確認する用途、zipは配布用の2用途を想定)。

    単一ファイル発行でも、WPFの描画・入力用ネイティブ相互運用DLL
    (wpfgfx_cor3.dll 等)はexeへ埋め込めずフォルダ内に残る。「exe 1個のみ」には
    ならない点に注意(詳細はREADME.mdのビルド手順節を参照)。

    バージョンは Directory.Build.props の <Version> を単一の情報源として使用し、
    本スクリプト内では重複定義しない。

.PARAMETER RuntimeIdentifier
    発行対象のランタイム識別子。既定は win-x64。

.PARAMETER Configuration
    ビルド構成。既定は Release。

.EXAMPLE
    pwsh ./scripts/publish-wpf-release.ps1
    pwsh ./scripts/publish-wpf-release.ps1 -RuntimeIdentifier win-arm64
#>
[CmdletBinding()]
param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/PdfBookmarkMerger.Wpf/PdfBookmarkMerger.Wpf.csproj"
$distDir = Join-Path $repoRoot "dist"

if (-not (Test-Path $project)) {
    throw "プロジェクトが見つかりません: $project"
}

$version = (dotnet msbuild $project "-getProperty:Version" -nologo).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Versionプロパティを取得できませんでした(Directory.Build.propsを確認してください)"
}

$baseName = "PdfBookmarkMerger-Wpf-v$version-$RuntimeIdentifier"
$publishDir = Join-Path $distDir $baseName
$zipPath = Join-Path $distDir "$baseName.zip"

Write-Host "=== 1/2: dotnet publish (単一ファイル・圧縮・PDB除外, Version=$version, RID=$RuntimeIdentifier) ===" -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish が失敗しました(終了コード: $LASTEXITCODE)"
}
if (-not (Test-Path $publishDir)) {
    throw "publish出力フォルダが見つかりません: $publishDir"
}

Write-Host "=== 2/2: zip圧縮 -> $zipPath ===" -ForegroundColor Cyan
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath

Write-Host "完了" -ForegroundColor Green
Get-Item $publishDir | Select-Object FullName, @{Name = "SizeMB"; Expression = { [math]::Round(((Get-ChildItem $_ -Recurse -File | Measure-Object -Property Length -Sum).Sum) / 1MB, 2) } }
Get-Item $zipPath | Select-Object FullName, @{Name = "SizeMB"; Expression = { [math]::Round($_.Length / 1MB, 2) } }
