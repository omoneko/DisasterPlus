# Minimal IL disassembler with token resolution.
# Requires $global:A (Assembly-CSharp) from ilload.ps1.

$global:OpMap = @{}
foreach ($f in [System.Reflection.Emit.OpCodes].GetFields('Public,Static')) {
    $op = $f.GetValue($null)
    $key = ([int]$op.Value) -band 0xFFFF   # two-byte opcodes are negative Int16 (0xFExx)
    $global:OpMap[$key] = $op
}

function Get-OperandSize([System.Reflection.Emit.OpCode]$op) {
    switch ($op.OperandType.ToString()) {
        'InlineNone'        { 0 }
        'ShortInlineBrTarget' { 1 }
        'ShortInlineI'      { 1 }
        'ShortInlineVar'    { 1 }
        'InlineVar'         { 2 }
        'InlineBrTarget'    { 4 }
        'InlineField'       { 4 }
        'InlineI'           { 4 }
        'InlineMethod'      { 4 }
        'InlineSig'         { 4 }
        'InlineString'      { 4 }
        'InlineTok'         { 4 }
        'InlineType'        { 4 }
        'ShortInlineR'      { 4 }
        'InlineI8'          { 8 }
        'InlineR'           { 8 }
        'InlineSwitch'      { -1 }
        default             { 0 }
    }
}

function Disasm-Method {
    param(
        [Parameter(Mandatory=$true)] [System.Reflection.MethodBase] $Method,
        [string] $Filter = $null
    )
    $body = $Method.GetMethodBody()
    if (-not $body) { Write-Host "  (no body)"; return }
    $il = $body.GetILAsByteArray()
    $mod = $Method.Module
    $tArgs = $null; $mArgs = $null
    try { if ($Method.DeclaringType.IsGenericType) { $tArgs = $Method.DeclaringType.GetGenericArguments() } } catch {}
    try { if ($Method.IsGenericMethod) { $mArgs = $Method.GetGenericArguments() } } catch {}

    $i = 0
    $out = New-Object System.Collections.ArrayList
    while ($i -lt $il.Length) {
        $offset = $i
        $b = $il[$i]; $i++
        $key = $b
        if ($b -eq 0xFE) { $key = 0xFE00 -bor $il[$i]; $i++ }
        $op = $global:OpMap[[int]$key]
        if (-not $op) { [void]$out.Add(("IL_{0:X4}:  <unknown 0x{1:X2}>" -f $offset, $b)); continue }

        $size = Get-OperandSize $op
        $text = ""
        if ($size -eq -1) {
            $n = [BitConverter]::ToInt32($il, $i); $i += 4
            $i += 4 * $n
            $text = "switch($n targets)"
        }
        elseif ($size -gt 0) {
            switch ($op.OperandType.ToString()) {
                'InlineField' {
                    $tok = [BitConverter]::ToInt32($il, $i)
                    try { $fi = $mod.ResolveField($tok, $tArgs, $mArgs); $text = "$($fi.DeclaringType.Name)::$($fi.Name)" }
                    catch { $text = "field(0x$($tok.ToString('X8')))" }
                }
                'InlineMethod' {
                    $tok = [BitConverter]::ToInt32($il, $i)
                    try { $mi = $mod.ResolveMethod($tok, $tArgs, $mArgs); $text = "$($mi.DeclaringType.Name)::$($mi.Name)" }
                    catch { $text = "method(0x$($tok.ToString('X8')))" }
                }
                'InlineType' {
                    $tok = [BitConverter]::ToInt32($il, $i)
                    try { $ty = $mod.ResolveType($tok, $tArgs, $mArgs); $text = $ty.Name } catch { $text = "type(0x$($tok.ToString('X8')))" }
                }
                'InlineTok' {
                    $tok = [BitConverter]::ToInt32($il, $i)
                    try { $mem = $mod.ResolveMember($tok, $tArgs, $mArgs); $text = "$($mem.DeclaringType.Name)::$($mem.Name)" } catch { $text = "tok(0x$($tok.ToString('X8')))" }
                }
                'InlineString' {
                    $tok = [BitConverter]::ToInt32($il, $i)
                    try { $text = '"' + $mod.ResolveString($tok) + '"' } catch { $text = "str" }
                }
                'InlineI'            { $text = [BitConverter]::ToInt32($il, $i) }
                # [sbyte]$byte は PowerShell では 127 超で例外になる（キャストは
                # ビットの再解釈ではなく範囲検査つき変換）。ldc.i4.s に負の即値が
                # 入っている実メソッドで逆アセンブルごと落ちるので手で折り返す。
                'ShortInlineI'       { $v = [int]$il[$i]; if ($v -gt 127) { $v -= 256 }; $text = $v }
                'InlineI8'           { $text = [BitConverter]::ToInt64($il, $i) }
                'ShortInlineR'       { $text = [BitConverter]::ToSingle($il, $i) }
                'InlineR'            { $text = [BitConverter]::ToDouble($il, $i) }
                'InlineVar'          { $text = [BitConverter]::ToUInt16($il, $i) }
                'ShortInlineVar'     { $text = $il[$i] }
                'InlineBrTarget'     { $text = "IL_{0:X4}" -f ($i + 4 + [BitConverter]::ToInt32($il, $i)) }
                # 同上。後方分岐（ループ）は必ず負のオフセットなので、こちらは
                # 折り返しを忘れると分岐先が数百バイト先の存在しない位置になる。
                'ShortInlineBrTarget'{ $o = [int]$il[$i]; if ($o -gt 127) { $o -= 256 }; $text = "IL_{0:X4}" -f ($i + 1 + $o) }
                default              { $text = "" }
            }
            $i += $size
        }
        $line = "IL_{0:X4}:  {1,-14} {2}" -f $offset, $op.Name, $text
        [void]$out.Add($line)
    }
    if ($Filter) { $out | Where-Object { $_ -match $Filter } } else { $out }
}
