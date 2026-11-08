param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not $SkipBuild) { & .\build.ps1; if ($LASTEXITCODE) { throw 'Build failed' } }
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$testRoot = Join-Path $PSScriptRoot ('obj\tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $testRoot, "$testRoot\stage" | Out-Null
& "$fw\csc.exe" /nologo "/out:$testRoot\CoreTests.exe" tests\CoreTests.cs
if ($LASTEXITCODE) { throw 'Core test compile failed' }
& "$testRoot\CoreTests.exe" "$PSScriptRoot\bin\Island.exe"
if ($LASTEXITCODE) { throw 'Core tests failed' }
& "$fw\csc.exe" /nologo /target:winexe "/out:$testRoot\Fixture.exe" /r:System.Xaml.dll "/r:$fw\WPF\PresentationFramework.dll" "/r:$fw\WPF\WindowsBase.dll" tests\UpdaterFixture.cs
if ($LASTEXITCODE) { throw 'Updater fixture compile failed' }
& "$fw\csc.exe" /nologo /target:winexe "/out:$testRoot\stage\Island.exe" tests\UpdatedStub.cs
if ($LASTEXITCODE) { throw 'Update stub compile failed' }
$original = [Convert]::ToBase64String([IO.File]::ReadAllBytes("$testRoot\Fixture.exe"))
$fixture = Start-Process -FilePath "$testRoot\Fixture.exe" -ArgumentList @('"' + "$PSScriptRoot\bin\Island.exe" + '"', '"' + "$testRoot\stage\Island.exe" + '"') -PassThru -WindowStyle Hidden
if (-not $fixture.WaitForExit(15000)) { throw 'Updater fixture did not exit' }
$deadline = [DateTime]::UtcNow.AddSeconds(20)
while (-not (Test-Path -LiteralPath "$testRoot\updated.txt") -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
if (-not (Test-Path -LiteralPath "$testRoot\updated.txt")) { throw "Updated app did not launch: $testRoot" }
if (([IO.File]::ReadAllText("$testRoot\updated.txt")) -ne '--restart') { throw 'Restart argument missing' }
if ([Convert]::ToBase64String([IO.File]::ReadAllBytes("$testRoot\Fixture.exe.previous")) -ne $original) { throw 'Previous executable was not preserved' }
if ([Reflection.AssemblyName]::GetAssemblyName("$testRoot\Fixture.exe").Version.ToString() -ne '1.3.0.0') { throw 'Wrong executable installed' }
Write-Host 'PASS real updater replacement, backup, and restart in isolated folder'
