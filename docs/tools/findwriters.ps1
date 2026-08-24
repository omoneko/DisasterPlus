# Find every method whose IL touches a given field.
# ASCII only (dot-sourced from arbitrary paths; PS 5.1 reads BOM-less UTF-8 as ANSI).
# Requires $global:A from ilload.ps1.
#
#   . docs/tools/ilload.ps1
#   . docs/tools/findwriters.ps1
#   Find-FieldUsers -Type DisasterInfo -Field m_finalRandomProbability
#
# Why this exists: "who else writes this?" decides whether a mod can set a
# vanilla field once, or has to keep re-applying it. Guessing that wrong is
# a silent failure - the value is simply overwritten a frame later.

function Find-FieldUsers {
    param(
        [Parameter(Mandatory=$true)] [string] $Type,
        [Parameter(Mandatory=$true)] [string] $Field,
        [switch] $WritesOnly
    )

    $fld = $global:A.GetType($Type).GetField($Field,
        [Reflection.BindingFlags]'Public,NonPublic,Instance,Static')
    if (-not $fld) { Write-Host "no such field"; return }

    $token = [BitConverter]::GetBytes($fld.MetadataToken)
    $flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'

    # 0x7D = stfld, 0x80 = stsfld, 0x7B = ldfld, 0x7E = ldsfld
    $write = @(0x7D, 0x80)
    $read  = @(0x7B, 0x7E)
    $want  = if ($WritesOnly) { $write } else { $write + $read }

    foreach ($t in $global:A.GetTypes()) {
        $methods = @()
        try { $methods += $t.GetMethods($flags) } catch {}
        try { $methods += $t.GetConstructors($flags) } catch {}

        foreach ($m in $methods) {
            $il = $null
            try { $b = $m.GetMethodBody(); if ($b) { $il = $b.GetILAsByteArray() } } catch {}
            if (-not $il) { continue }

            for ($i = 0; $i -lt $il.Length - 4; $i++) {
                if ($want -notcontains $il[$i]) { continue }
                if ($il[$i+1] -ne $token[0]) { continue }
                if ($il[$i+2] -ne $token[1]) { continue }
                if ($il[$i+3] -ne $token[2]) { continue }
                if ($il[$i+4] -ne $token[3]) { continue }

                $kind = if ($write -contains $il[$i]) { 'WRITE' } else { 'read ' }
                "{0}  {1}::{2}  (IL_{3:X4})" -f $kind, $t.Name, $m.Name, $i
                break
            }
        }
    }
}
