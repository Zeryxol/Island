param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not $SkipBuild) { & .\build.ps1; if ($LASTEXITCODE) { throw 'Build failed' } }
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Force obj | Out-Null
& "$fw\csc.exe" /nologo /out:obj\SelectionTests.exe /r:System.Xaml.dll "/r:$fw\WPF\PresentationFramework.dll" "/r:$fw\WPF\PresentationCore.dll" "/r:$fw\WPF\WindowsBase.dll" tests\SelectionTests.cs
if ($LASTEXITCODE) { throw 'Selection tests compile failed' }
foreach ($mode in @('both')) {
    $report = Join-Path $PSScriptRoot "obj\selection-$mode.txt"
    $test = Start-Process -FilePath "$PSScriptRoot\obj\SelectionTests.exe" -ArgumentList @('"' + "$PSScriptRoot\bin\Island.exe" + '"', '"' + $report + '"', $mode) -PassThru -WindowStyle Normal
    if (-not $test.WaitForExit(40000)) { throw "Selection test timed out: $mode" }
    Get-Content -LiteralPath $report -Encoding UTF8
    if ($test.ExitCode -ne 0) { throw "Selection test failed: $mode" }
}
