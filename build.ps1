$ErrorActionPreference = "Stop"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe |
           Select-Object -First 1
if (-not $msbuild) { throw "MSBuild not found" }

# PackageReference（CitiesHarmony.API）は復元が要るので Restore を足す。
& $msbuild "src\DisasterPlus\DisasterPlus.csproj" /t:Restore,Build /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$modDir = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines\Addons\Mods\DisasterPlus"
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
Copy-Item "src\DisasterPlus\bin\Release\DisasterPlus.dll" $modDir -Force
Write-Host "Deployed DisasterPlus.dll -> $modDir"

# CitiesHarmony.API.dll はこの shim だけ MOD 同梱が正しい。
# HarmonyLib 本体（CitiesHarmony.Harmony.dll）は CitiesHarmony MOD が実行時に供給するので同梱しない。
# HarmonyBootstrap / VortexPinPatch はこのアセンブリに実行時依存するので、
# 無いまま「デプロイ成功」を装って終了してはいけない（ビルドは成功したのに MOD がロードで落ちる事故になる）。
$apiDll = "src\DisasterPlus\bin\Release\CitiesHarmony.API.dll"
if (Test-Path $apiDll) {
    Copy-Item $apiDll $modDir -Force
    Write-Host "Deployed CitiesHarmony.API.dll"
} else {
    throw "CitiesHarmony.API.dll not found in build output; HarmonyBootstrap/VortexPinPatch would fail at runtime"
}

# LocaleLoader は実行時に Locales\<lang>.txt を読む。
if (Test-Path "Locales") {
    $dst = Join-Path $modDir "Locales"
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Copy-Item "Locales\*" $dst -Force
    Write-Host "Deployed Locales"
} else {
    Write-Host "Note: Locales\ not found; skipped (added in a later task)."
}
