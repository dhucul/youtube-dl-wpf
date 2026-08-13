$amp = [char]38
$ampEntity = $amp + 'amp;'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectDirectory = Join-Path $repositoryRoot 'src\YoutubeDlModern'
$s = Join-Path $projectDirectory 'Views\SettingsView.xaml'
$text = [System.IO.File]::ReadAllText($s)
# Escape every remaining bare " & " (attribute text and comments alike; harmless in comments).
$text = $text.Replace(' ' + [char]38 + ' ', ' ' + $ampEntity + ' ')
[System.IO.File]::WriteAllText($s, $text, (New-Object System.Text.UTF8Encoding($false)))

# Verify: report remaining raw & that are not part of an entity, across all XAML files.
$found = $false
Get-ChildItem -LiteralPath $projectDirectory -Recurse -Filter *.xaml | ForEach-Object {
    $lines = [System.IO.File]::ReadAllLines($_.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $stripped = [regex]::Replace($lines[$i], [char]38 + '(amp|lt|gt|quot|apos|#[0-9A-Fa-fx]+);', '')
        if ($stripped.Contains([string][char]38)) {
            Write-Output ('RAW-AMP ' + $_.Name + ':' + ($i + 1) + ': ' + $lines[$i].Trim())
            $found = $true
        }
    }
}
if (-not $found) { Write-Output 'all clear' }
