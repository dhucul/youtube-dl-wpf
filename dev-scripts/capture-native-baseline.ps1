[CmdletBinding()]
param(
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string] $Revision = '51bab8a0116f4d8004c315706d809782607d5847',
    [string] $CapturedOn = '2026-10-07'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$baselineDirectory = Join-Path $repositoryRoot 'docs\native-engine'
$headers = @{ 'User-Agent' = 'YoutubeDlModern-CompatibilityInventory' }
$commit = Invoke-RestMethod -Uri "https://api.github.com/repos/yt-dlp/yt-dlp/commits/$Revision" -Headers $headers
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest' -Headers $headers
$catalogUrl = "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$Revision/supportedsites.md"
$catalog = [string](Invoke-WebRequest -Uri $catalogUrl).Content
$extractors = @(
    foreach ($line in ($catalog -split '\r?\n')) {
        if ($line -match '^\s*-\s+\*\*(?<key>.+?)\*\*(?::\s*(?<description>.*))?$') {
            [pscustomobject][ordered]@{
                key = $Matches.key
                description = $Matches.description
                upstreamKnownBroken = $line.Contains('Currently broken')
            }
        }
    }
)
if ($extractors.Count -lt 500) { throw 'The catalog is unexpectedly small; refusing to replace the baseline.' }
$duplicateKeys = $extractors | Group-Object -Property key -CaseSensitive | Where-Object Count -gt 1
if ($duplicateKeys) { throw 'The upstream catalog contains duplicate keys; review it before recording the baseline.' }

$baseline = [ordered]@{
    schemaVersion = 1
    capturedOn = $CapturedOn
    upstreamRevision = $Revision
    upstreamCommitDate = $commit.commit.committer.date
    latestStableReleaseAtCapture = $release.tag_name
    source = $catalogUrl
    sourceSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($catalog))).ToLowerInvariant()
    extractorCount = $extractors.Count
    upstreamKnownBrokenCount = @($extractors | Where-Object upstreamKnownBroken).Count
    notes = 'Extractor keys are not a count of distinct websites. This catalog is a scope inventory, not a claim of native implementation. Generic and embedded-media behavior also remains in scope.'
    extractors = $extractors
}

$settingsSource = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'src\YoutubeDlModern\Models\AppSettings.cs'))
$features = @(
    foreach ($category in [regex]::Matches($settingsSource, 'public partial class (?<name>\w+)Settings\s*:\s*ObservableObject(?<body>.*?)(?=public partial class |\z)', 'Singleline')) {
        foreach ($field in [regex]::Matches($category.Groups['body'].Value, '\[ObservableProperty\]\s+private\s+(?<type>\S+)\s+_(?<name>\w+)')) {
            $fieldName = $field.Groups['name'].Value
            [ordered]@{
                id = $category.Groups['name'].Value + '.' + [char]::ToUpperInvariant($fieldName[0]) + $fieldName.Substring(1)
                category = $category.Groups['name'].Value
                dataType = $field.Groups['type'].Value
                status = 'pending'
            }
        }
    }
)
$downloadSource = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'src\YoutubeDlModern\Models\DownloadOptions.cs'))
foreach ($model in [regex]::Matches($downloadSource, 'public sealed class (?<name>DownloadOptions|AnalysisOptions)(?<body>.*?)(?=public sealed class |\z)', 'Singleline')) {
    foreach ($property in [regex]::Matches($model.Groups['body'].Value, 'public\s+(?<type>\S+)\s+(?<name>\w+)\s*\{\s*get;\s*set;')) {
        $features += [ordered]@{
            id = $model.Groups['name'].Value + '.' + $property.Groups['name'].Value
            category = $model.Groups['name'].Value
            dataType = $property.Groups['type'].Value
            status = 'pending'
        }
    }
}
$upstreamReadmeUrl = "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$Revision/README.md"
$upstreamReadme = [string](Invoke-WebRequest -Uri $upstreamReadmeUrl).Content
$usage = [regex]::Match($upstreamReadme, '(?ms)^# USAGE AND OPTIONS\s*(?<body>.*?)^# CONFIGURATION\s*$').Groups['body'].Value
if ([string]::IsNullOrWhiteSpace($usage)) { throw 'The upstream usage documentation could not be located.' }
$optionNames = [regex]::Matches($usage, '(?<![a-z0-9-])--[a-z][a-z0-9-]*') | ForEach-Object Value | Sort-Object -Unique
if (@($optionNames).Count -lt 100) { throw 'The upstream option inventory is unexpectedly small.' }
New-Item -ItemType Directory -Path $baselineDirectory -Force | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $baselineDirectory 'upstream-catalog.json'), ($baseline | ConvertTo-Json -Depth 8), $encoding)
[IO.File]::WriteAllText((Join-Path $baselineDirectory 'feature-matrix.json'), ([ordered]@{
    schemaVersion = 1
    capturedOn = $CapturedOn
    sources = @('src/YoutubeDlModern/Models/AppSettings.cs', 'src/YoutubeDlModern/Models/DownloadOptions.cs')
    featureCount = $features.Count
    notes = 'Every app setting, analysis option, and per-download option needs an explicit native implementation and migration decision. See upstream-options.json for the upstream CLI option inventory.'
    features = $features
} | ConvertTo-Json -Depth 8), $encoding)
[IO.File]::WriteAllText((Join-Path $baselineDirectory 'upstream-options.json'), ([ordered]@{
    schemaVersion = 1
    capturedOn = $CapturedOn
    upstreamRevision = $Revision
    source = $upstreamReadmeUrl
    optionCount = @($optionNames).Count
    notes = 'Long option names advertised in upstream usage documentation. Aliases, defaults, interactions, selector/template grammars, and extractor-specific behavior need additional specifications. Naming an option here does not claim an implementation.'
    options = @($optionNames | ForEach-Object { [ordered]@{ name = $_; status = 'pending' } })
} | ConvertTo-Json -Depth 8), $encoding)
Write-Output "Recorded $($extractors.Count) upstream extractors, $($features.Count) app options, and $(@($optionNames).Count) upstream option names."
