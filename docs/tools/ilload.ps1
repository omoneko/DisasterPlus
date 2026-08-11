# Loader for Assembly-CSharp.dll per cities-skylines-modding/pitfalls.md#reading-il
# ReflectionOnlyLoadFrom cannot give GetMethodBody(), so load normally.
$ErrorActionPreference = "Stop"
$dir = 'C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed'
$global:preloaded = @{}
foreach ($n in @('UnityEngine','ColossalManaged','ICities','Assembly-CSharp-firstpass','UnityEngine.UI')) {
  $f = Join-Path $dir ($n + '.dll')
  if (Test-Path $f) {
    try { $asm = [System.Reflection.Assembly]::LoadFrom($f); $global:preloaded[$asm.GetName().Name] = $asm } catch {}
  }
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({
  param($s, $e)
  $n = (New-Object System.Reflection.AssemblyName $e.Name).Name
  if ($global:preloaded.ContainsKey($n)) { return $global:preloaded[$n] }
  return $null   # never LoadFrom here: infinite recursion -> StackOverflow
})
$global:A = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir 'Assembly-CSharp.dll'))
Write-Host "Loaded Assembly-CSharp OK"
