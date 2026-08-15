# Locale gate. Runs from build.ps1 BEFORE the files are copied to the mod folder.
#
# Why this exists: ja.txt shipped a heading that told the player to look for the
# marker "[measured]", while ja.txt's own SourceVanilla is a different string.
# English happened to match, so nobody could see it by reading the English side.
# A key-set diff alone would not have caught it either - both files had the key.
# So this script checks key parity AND the marker contract.
#
# ASCII ONLY. Windows PowerShell 5.1 reads a BOM-less UTF-8 .ps1 as ANSI, which
# turns any non-ASCII byte into mojibake and can break the parse. Keep every
# character in this file inside 0x00-0x7F. (The locale files themselves are read
# with an explicit UTF-8 decode below, so their Japanese content is fine.)
$ErrorActionPreference = "Stop"

$localeDir = Join-Path (Get-Location) "Locales"
if (-not (Test-Path $localeDir)) {
    Write-Host "Locales/ not found; locale check skipped."
    exit 0
}

# The reference is en.txt: it is generated from the built DLL by
# tools\GenerateLocaleTemplate.ps1, so it is the authority on the key set.
$reference = Join-Path $localeDir "en.txt"
if (-not (Test-Path $reference)) { throw "Locales\en.txt not found" }

function Read-Locale([string]$path) {
    # File.ReadAllLines with an explicit UTF-8 decode, matching LocaleLoader.
    $map = New-Object 'System.Collections.Specialized.OrderedDictionary'
    $dups = New-Object System.Collections.Generic.List[string]
    foreach ($raw in [IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line[0] -eq '#') { continue }
        $eq = $line.IndexOf('=')
        if ($eq -le 0) { continue }
        $key = $line.Substring(0, $eq).Trim()
        # LocaleLoader trims the value, so compare what the game will actually use.
        $value = $line.Substring($eq + 1).Trim()
        if ($map.Contains($key)) { $dups.Add($key) } else { $map.Add($key, $value) }
    }
    return @{ Map = $map; Dups = $dups }
}

$problems = New-Object System.Collections.Generic.List[string]

$en = Read-Locale $reference
$enKeys = @($en.Map.Keys)
if ($en.Dups.Count -gt 0) { $problems.Add("en.txt: duplicate keys: " + ($en.Dups -join ', ')) }

# The marker token that the panel heading uses. Must stay in sync with
# Strings.MeasuredToken (internal const) - the check below is what keeps it honest.
$token = '{measured}'

foreach ($file in Get-ChildItem -Path $localeDir -Filter *.txt) {
    $name = $file.Name

    # BOM check: LocaleLoader decodes as UTF-8; a BOM would corrupt the first key.
    $head = [IO.File]::ReadAllBytes($file.FullName)
    if ($head.Length -ge 3 -and $head[0] -eq 0xEF -and $head[1] -eq 0xBB -and $head[2] -eq 0xBF) {
        $problems.Add("$name has a UTF-8 BOM; locale files must be BOM-less")
    }

    $loc = Read-Locale $file.FullName
    if ($loc.Dups.Count -gt 0) { $problems.Add("$name : duplicate keys: " + ($loc.Dups -join ', ')) }

    if ($name -ne "en.txt") {
        $missing = @($enKeys | Where-Object { -not $loc.Map.Contains($_) })
        $unknown = @(@($loc.Map.Keys) | Where-Object { $enKeys -notcontains $_ })
        if ($missing.Count -gt 0) { $problems.Add("$name : missing keys: " + ($missing -join ', ')) }
        if ($unknown.Count -gt 0) { $problems.Add("$name : unknown keys: " + ($unknown -join ', ')) }
    }

    # --- marker contract -------------------------------------------------
    # 1. Every heading that explains a panel's display convention must carry the
    #    token, because the marker itself is substituted at runtime from
    #    SourceVanilla. One entry per panel that names the marker in prose.
    #    Add the new key here when a feature adds such a heading; leaving it out
    #    is exactly how the original bug shipped.
    foreach ($noteKey in @('TyphoonModelNote', 'VolcanoModelNote')) {
        if ($loc.Map.Contains($noteKey)) {
            $note = [string]$loc.Map[$noteKey]
            if ($note.IndexOf($token) -lt 0) {
                $problems.Add("$name : $noteKey does not contain the $token token, so the " +
                              "panel heading will never name the marker the rows actually carry")
            }
        }
    }

    # 2. No translated value may hard-code a marker literal. That is exactly the
    #    bug this file exists for: a note that spelled out one language's marker
    #    while the rows carried another's.
    $vanilla = if ($loc.Map.Contains('SourceVanilla')) { [string]$loc.Map['SourceVanilla'] } else { $null }
    $model = if ($loc.Map.Contains('SourceModel')) { [string]$loc.Map['SourceModel'] } else { $null }
    foreach ($key in @($loc.Map.Keys)) {
        if ($key -eq 'SourceVanilla' -or $key -eq 'SourceModel') { continue }
        $value = [string]$loc.Map[$key]
        # Only bracketed markers count; ordinary square brackets in prose are fine.
        if ($vanilla -and $vanilla.Length -gt 0 -and $value.Contains($vanilla)) {
            $problems.Add("$name : $key hard-codes the marker $vanilla; use $token instead")
        }
        if ($model -and $model.Length -gt 0 -and $value.Contains($model)) {
            $problems.Add("$name : $key hard-codes the marker $model; markers are added at runtime")
        }
        # The English marker is the one that leaked into ja.txt. Catch it by name
        # in every file, whatever that file's own marker happens to be.
        if ($value.Contains('[measured]')) {
            $problems.Add("$name : $key hard-codes the English marker [measured]; use $token instead")
        }
    }
}

if ($problems.Count -gt 0) {
    foreach ($p in $problems) { Write-Host ("LOCALE: " + $p) }
    throw "Locale check failed ($($problems.Count) problem(s))"
}

Write-Host "Locales OK ($($enKeys.Count) keys, $((Get-ChildItem -Path $localeDir -Filter *.txt).Count) file(s))"
