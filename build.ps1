$ErrorActionPreference = "Stop"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe |
           Select-Object -First 1
if (-not $msbuild) { throw "MSBuild not found" }

# The PackageReference (CitiesHarmony.API) has to be restored, so Restore is added here.
& $msbuild "src\DisasterPlus\DisasterPlus.csproj" /t:Restore,Build /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# CitiesHarmony.API.dll is the one shim that is correct to ship with the mod.
# HarmonyLib itself (CitiesHarmony.Harmony.dll) is supplied at run time by the CitiesHarmony
# mod, so it is not shipped.
# HarmonyBootstrap / VortexPinPatch depend on this assembly at run time, so we must never
# finish here pretending the deployment succeeded while it is missing (that turns into the
# accident where the build succeeded but the mod dies on load).
#
# This check must always be done BEFORE DisasterPlus.dll is copied. Put it after, and the
# moment it throws all that is left in the deployment folder is a DisasterPlus.dll that cannot
# resolve Harmony, and that broken pair is what gets loaded on the next launch.
$apiDll = "src\DisasterPlus\bin\Release\CitiesHarmony.API.dll"
if (-not (Test-Path $apiDll)) {
    throw "CitiesHarmony.API.dll not found in build output; HarmonyBootstrap/VortexPinPatch would fail at runtime"
}

$modDir = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines\Addons\Mods\DisasterPlus"
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
Copy-Item "src\DisasterPlus\bin\Release\DisasterPlus.dll" $modDir -Force
Write-Host "Deployed DisasterPlus.dll -> $modDir"

Copy-Item $apiDll $modDir -Force
Write-Host "Deployed CitiesHarmony.API.dll"

# ★ Check **before** copying Locales. ja.txt told the reader "only the lines carrying
#   [measured]", while its own SourceVanilla said [実測] (in English the two happen to
#   coincide, so reading the English side would never reveal it). Counting the keys alone
#   does not catch that, so on top of the key sets matching we check the marker contract too.
#   So that nothing is ever deployed broken, throw here and stop every copy that follows.
& powershell -NoProfile -ExecutionPolicy Bypass -File "tools\CheckLocales.ps1"
if ($LASTEXITCODE -ne 0) { throw "Locale check failed" }

# LocaleLoader reads Locales\<lang>.txt at run time.
if (Test-Path "Locales") {
    $dst = Join-Path $modDir "Locales"
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Copy-Item "Locales\*" $dst -Force
    Write-Host "Deployed Locales"
} else {
    Write-Host "Note: Locales\ not found; skipped (added in a later task)."
}

# VolcanoEruptionAudio reads Audio\erupting-volcano.wav at run time, from the mod folder.
# It is deployed exactly the way Locales\ is: copied next to the DLL, read by name.
#
# Do NOT throw when it is missing. The mod is written so that a missing or unreadable
# wav means "the eruption is silent" and nothing else - a build that refuses to deploy
# would be stricter than the running mod, and would block anyone who deleted the file
# on purpose (it is ~6 MB of audio in a Workshop item).
if (Test-Path "Audio") {
    $audioDst = Join-Path $modDir "Audio"
    New-Item -ItemType Directory -Force -Path $audioDst | Out-Null
    Copy-Item "Audio\*" $audioDst -Force
    Write-Host "Deployed Audio"
} else {
    Write-Host "Note: Audio\ not found; the eruption will be silent."
}
