$ErrorActionPreference = "Stop"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe |
           Select-Object -First 1
if (-not $msbuild) { throw "MSBuild not found" }

& $msbuild "src\DisasterPlus\DisasterPlus.csproj" /t:Build /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$modDir = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines\Addons\Mods\DisasterPlus"
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
Copy-Item "src\DisasterPlus\bin\Release\DisasterPlus.dll" $modDir -Force
Write-Host "Deployed DisasterPlus.dll -> $modDir"

# LocaleLoader は実行時に Locales\<lang>.txt を読む。
if (Test-Path "Locales") {
    $dst = Join-Path $modDir "Locales"
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Copy-Item "Locales\*" $dst -Force
    Write-Host "Deployed Locales"
} else {
    Write-Host "Note: Locales\ not found; skipped (added in a later task)."
}
