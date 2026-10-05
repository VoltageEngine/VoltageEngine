param([Parameter(Mandatory=$true)][string]$AsepritePath)
$ErrorActionPreference = 'Stop'
$resourcePath = Join-Path $PSScriptRoot '../../Voltage.Editor/Aseprite/Resources/plugin.lua'
$source = [System.IO.File]::ReadAllText($resourcePath).Replace('__VOLTAGE_PORT__', '6520').Replace('__VOLTAGE_TOKEN__', ('0' * 64))
$checks = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'smoke.lua'))
$temporaryScript = Join-Path ([System.IO.Path]::GetTempPath()) ('voltage-aseprite-check-' + [guid]::NewGuid().ToString('N') + '.lua')
try {
    [System.IO.File]::WriteAllText($temporaryScript, $source + "`n" + $checks)
    $start = [System.Diagnostics.ProcessStartInfo]::new($AsepritePath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add('--batch')
    $start.ArgumentList.Add('--script')
    $start.ArgumentList.Add($temporaryScript)
    $process = [System.Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Aseprite checks timed out' }
    $result = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    Write-Output $result
    if ($process.ExitCode -ne 0 -or $result -notmatch 'VOLTAGE_ASEPRITE_SMOKE_OK') { throw 'Aseprite Lua smoke checks failed' }
} finally {
    Remove-Item -LiteralPath $temporaryScript -ErrorAction SilentlyContinue
}
