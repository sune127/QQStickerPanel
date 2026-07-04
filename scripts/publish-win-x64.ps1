param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [ValidateSet("FrameworkDependentSingleFile", "SelfContainedSingleFile")]
    [string]$DeploymentMode = "FrameworkDependentSingleFile"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\QQStickerPanel\QQStickerPanel.csproj"
$isSelfContained = $DeploymentMode -eq "SelfContainedSingleFile"
$enableCompression = $isSelfContained
$outputName = if ($isSelfContained) { "QQStickerPanel-$Runtime-self-contained" } else { "QQStickerPanel-$Runtime-framework-dependent" }
$outputPath = Join-Path $repoRoot "artifacts\$outputName"
$publishedExePath = Join-Path $outputPath "QQStickerPanel.exe"

if (Test-Path $publishedExePath) {
    try {
        $stream = [System.IO.File]::Open($publishedExePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $stream.Dispose()
    }
    catch [System.IO.IOException] {
        Write-Error ("Publish failed: {0} is in use. Please close QQStickerPanel and retry." -f $publishedExePath)
        exit 1
    }
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained $isSelfContained `
    --output $outputPath `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=$enableCompression

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($isSelfContained) {
    Write-Host "Published self-contained single-file build to $outputPath"
}
else {
    Write-Host "Published framework-dependent single-file build to $outputPath"
    Write-Host "Target machines must have Microsoft .NET 8 Windows Desktop Runtime x64 installed."
}
