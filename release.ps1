# ============ Per-mod release pipeline (self-contained) ============
# Usage: .\release.ps1 [-Yes] [-PackageOnly]
#   (default)  dry-run: all gates + build + package + update.json; NO tag/release/COS
#   -Yes       full release: dry-run steps + versions ledger + tag/GitHub release + COS upload
#   -PackageOnly  build+package+update.json only (suite publish uses this); no CHANGELOG/dirty-tree gate
# Version authority: csproj <Version> (author-edited); Core/Ds.cs Version const must match.
# Art gate: exactly 45 Art\ds_*.webp icons (one per catalog key) must exist before build.
param([switch]$Yes, [switch]$PackageOnly)
$ErrorActionPreference = 'Stop'
$repoDir = $PSScriptRoot
$root    = Split-Path $PSScriptRoot -Parent
$modName = Split-Path $repoDir -Leaf
$proj    = Join-Path $repoDir ('SurvivalLog.' + $modName)
$dotnet  = Join-Path $root 'sdk\dotnet\dotnet.exe'
$ghExe   = Join-Path $root 'suite\tools\gh\bin\gh.exe'
$org     = 'survivallog-mods'

# ---- 1. version authority: csproj ----
$csproj = Join-Path $proj ('SurvivalLog.' + $modName + '.csproj')
$t = [IO.File]::ReadAllText($csproj)
$ver = [regex]::Match($t, '<Version>([\d.]+)</Version>').Groups[1].Value
if (-not $ver) { throw 'cannot read <Version> from csproj' }
$dsCs = Join-Path $proj 'Core\Ds.cs'
if (([IO.File]::ReadAllText($dsCs)) -notmatch ('public const string Version\s*=\s*"' + [regex]::Escape($ver) + '"')) {
    throw ('Core/Ds.cs Version const != csproj ' + $ver)
}

# ---- 2. gates (release mode only) ----
# CHANGELOG.md lives inside the project dir in this mirror-repo layout (synced from mod_stack)
$logPath = Join-Path $proj 'CHANGELOG.md'
$logTxt = [IO.File]::ReadAllText($logPath)
if (-not $PackageOnly) {
    if ($logTxt -notmatch (('##\s*v' + [regex]::Escape($ver)) + '\b')) { throw ('CHANGELOG.md has no v' + $ver + ' section') }
    $dirty = @(git -C $repoDir status --porcelain)
    $blocking = @($dirty | Where-Object { $_ -notmatch 'CHANGELOG\.md\s*$' })
    if ($blocking.Count -gt 0) { $blocking | ForEach-Object { Write-Host ('  ' + $_) }; throw 'uncommitted changes - commit or stash first (CHANGELOG.md allowed)' }
    if (-not $Yes) { Write-Host ('DRY-RUN for ' + $modName + ' v' + $ver + ' (tag/release/COS skipped; pass -Yes to publish)') }
}

# ---- 3. art gate: exactly 45 catalog icons (before build) ----
$keys = @()
$keys += 1..5 | ForEach-Object { 'n' + $_ }   # plain N1..N5
$keys += 1..8 | ForEach-Object { 'g' + $_ }   # good common G1..G8
$keys += 1..6 | ForEach-Object { 'r' + $_ }   # good rare R1..R6
$keys += 1..5 | ForEach-Object { 'e' + $_ }   # good epic E1..E5
$keys += 1..3 | ForEach-Object { 'l' + $_ }   # good legendary L1..L3
$keys += 1..6 | ForEach-Object { 'b' + $_ }   # bad light B1..B6
$keys += 1..5 | ForEach-Object { 'm' + $_ }   # bad mid M1..M5
$keys += 1..4 | ForEach-Object { 'h' + $_ }   # bad heavy H1..H4
$keys += 1..3 | ForEach-Object { 'd' + $_ }   # bad disaster D1..D3
$artDir = Join-Path $proj 'Art'
$expected = @($keys | ForEach-Object { 'ds_' + $_ + '.webp' })
$actual = @(Get-ChildItem $artDir -Filter 'ds_*.webp' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
$missing = @($expected | Where-Object { $actual -notcontains $_ })
$stray   = @($actual | Where-Object { $expected -notcontains $_ })
if ($missing.Count -gt 0 -or $stray.Count -gt 0 -or $actual.Count -ne 45) {
    if ($missing.Count -gt 0) { $missing | ForEach-Object { Write-Host ('  missing: ' + $_) } }
    if ($stray.Count -gt 0)   { $stray   | ForEach-Object { Write-Host ('  stray:   ' + $_) } }
    throw ('art gate failed: expected exactly 45 ds_*.webp under Art\, found ' + $actual.Count)
}
Write-Host ('art gate ok: 45/45 icons present')

# ---- 4. build ----
$env:DOTNET_MULTILEVEL_LOOKUP = '0'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
& $dotnet build $proj -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw ('build failed: ' + $modName) }
$dll = Join-Path $proj ('bin\Release\SurvivalLog.' + $modName + '.dll')
if (-not (Test-Path $dll)) { throw ('dll missing: ' + $dll) }

# ---- 5. package: single plugin zip (BepInEx\plugins\dll + README), suite naming; no BepInEx base ----
$relDir = Join-Path $repoDir 'release'
New-Item $relDir -ItemType Directory -Force | Out-Null
$pkg = Join-Path $env:TEMP ('slpkg_' + $modName + '_v' + $ver)
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item (Join-Path $pkg 'BepInEx\plugins') -ItemType Directory -Force | Out-Null
Copy-Item $dll (Join-Path $pkg 'BepInEx\plugins') -Force
# no preset cfg (first run generates defaults)
Copy-Item (Join-Path $repoDir 'ReleaseREADME.md') (Join-Path $pkg 'README.md') -Force
$zip = Join-Path $relDir ('SurvivalLog.' + $modName + '_v' + $ver + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip -CompressionLevel Optimal

# ---- 6. update.json (contract: mod/version/asset/md5/gamekb) ----
# The zip is the primary asset (panel install/update downloads it). CHANGELOG section of this
# version is the single source of release notes -> update.json.notes + GitHub body.
$md5 = (Get-FileHash $zip -Algorithm MD5).Hash.ToLower()
$gk = Get-ChildItem (Join-Path $root 'gamekb') -Directory | Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}_\d+$' } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $gk) { throw 'no gamekb snapshot under <root>\gamekb' }
$notes = [regex]::Match($logTxt, ('(?s)##\s*v' + [regex]::Escape($ver) + '\b.*?(?=\r?\n(---|#{1,2}\s)|\z)')).Value.Trim()
$uj = @{ mod = $modName; version = $ver; asset = (Split-Path $zip -Leaf); md5 = $md5; gamekb = $gk.Name; notes = $notes } | ConvertTo-Json
$updatePath = Join-Path $env:TEMP 'update.json'
[IO.File]::WriteAllText($updatePath, $uj, (New-Object Text.UTF8Encoding($false)))
Write-Host $uj
if ($PackageOnly) { Write-Host ('package-only done: ' + $zip); exit 0 }
if (-not $Yes) { Write-Host ('=== dry-run done: ' + $zip + ' ==='); exit 0 }

# ---- 7. versions ledger (fetch old versions.json before gh release create) ----
# 404 = first release (start fresh); any other failure aborts (a network glitch must
# not silently reset the full history ledger; GitHub release not yet created = no half state).
$cosBase = 'https://survival-log-1302318873.cos.ap-shanghai.myqcloud.com'
$versPath = Join-Path $env:TEMP ('versions_' + $modName + '.json')
$vrows = New-Object System.Collections.Generic.List[object]
try {
    $vr = Invoke-WebRequest -UseBasicParsing -Uri ($cosBase + '/mods/' + $modName + '/versions.json')
    # PS5.1 无 charset 响应按 ISO-8859-1 解 .Content（2026-11-15 实证 mojibake）——字节读 + UTF-8
    $vtxt = [Text.Encoding]::UTF8.GetString($vr.RawContentStream.ToArray())
    foreach ($r in @(($vtxt | ConvertFrom-Json).versions)) { if ($null -eq $r) { continue }; if ($r.version -ne $ver) { $vrows.Add($r) } }
} catch {
    $code = 0
    if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { $code = [int]$_.Exception.Response.StatusCode }
    if ($code -ne 404) { throw ('failed to fetch versions ledger (non-404, abort to avoid silent reset): ' + $_.Exception.Message) }
    # 404 = first release
}
$vrows.Insert(0, [ordered]@{ version = $ver; date = (Get-Date -Format 'yyyy-MM-dd'); notes = $notes; asset = (Split-Path $zip -Leaf); md5 = $md5; gamekb = $gk.Name })
[IO.File]::WriteAllText($versPath, ([ordered]@{ generatedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); mod = $modName; versions = $vrows } | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))

# ---- 8. tag + GitHub release (zip + update.json) ----
$notesFile = Join-Path $env:TEMP ('relnotes_' + $modName + '_v' + $ver + '.md')
[IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding($false)))
& $ghExe release create ('v' + $ver) --repo ($org + '/' + $modName) --title ('v' + $ver) --notes-file $notesFile $zip $updatePath
if ($LASTEXITCODE -ne 0) { throw ('gh release create failed: ' + $modName) }

# ---- 9. COS mirror (zip + update.json + versions.json -> mods/<mod>/) ----
# Failure does not abort the release (GitHub is the fallback source; players see the
# old version for now); remediation hint printed. Upload zip first, update.json last.
try {
    & (Join-Path $root 'suite\tools\sync-cos.ps1') -Mod $modName -Zip $zip -UpdateJson $updatePath -Versions $versPath
} catch {
    Write-Warning ('COS upload failed (GitHub release already live, players see old version for now): ' + $_.Exception.Message)
    Write-Warning ('remediation: suite\tools\sync-cos.ps1 -Mod ' + $modName + ' -Zip ' + $zip + ' -UpdateJson ' + $updatePath + ' -Versions ' + $versPath)
    Write-Warning ('then re-run suite root publish-index.ps1 to refresh the aggregate index')
}
Write-Host ('=== ' + $modName + ' v' + $ver + ' released ===')
Write-Host 'Nexus Mods stays a separate author-confirmed step (docs/08). Not automated.'
