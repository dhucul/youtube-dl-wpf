[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\YoutubeDlModern\YoutubeDlModern.csproj'
$stagingRoot = Join-Path $repositoryRoot '.installer-staging'
$publishPath = Join-Path $stagingRoot ([Guid]::NewGuid().ToString('N'))
$installerScript = Join-Path $repositoryRoot 'installer\YoutubeDlModern.iss'
$innoCompiler = 'C:\Program Files\Inno Setup 7\ISCC.exe'

if (-not (Test-Path -LiteralPath $innoCompiler -PathType Leaf)) {
    throw "Inno Setup compiler not found at '$innoCompiler'."
}

New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
try {
    dotnet publish $projectPath `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishPath `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $publishDefine = '/DPublishDir=' + $publishPath
    & $innoCompiler $publishDefine $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }

    $installerPath = Join-Path $repositoryRoot 'installer\output\YoutubeDL-Modern-Setup-1.0.0-x64.exe'
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "Installer output was not created at '$installerPath'."
    }
    Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
}
finally {
    $resolvedStagingRoot = [System.IO.Path]::GetFullPath($stagingRoot)
    $resolvedPublishPath = [System.IO.Path]::GetFullPath($publishPath)
    $requiredPrefix = $resolvedStagingRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPublishPath.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove installer staging outside '$resolvedStagingRoot'."
    }
    if (Test-Path -LiteralPath $resolvedPublishPath) {
        Remove-Item -LiteralPath $resolvedPublishPath -Recurse -Force
    }
    if ((Test-Path -LiteralPath $resolvedStagingRoot) -and
        -not (Get-ChildItem -LiteralPath $resolvedStagingRoot -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $resolvedStagingRoot -Force
    }
}
