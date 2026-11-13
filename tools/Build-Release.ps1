$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $root '.build/dotnet'
$env:NUGET_PACKAGES = Join-Path $root '.build/packages'
function Assert-Dotnet { if ($LASTEXITCODE -ne 0) { throw "dotnet falhou com codigo $LASTEXITCODE" } }
dotnet restore CursedPasswordManager.slnx --locked-mode
Assert-Dotnet
dotnet build CursedPasswordManager.slnx -c Release --no-restore
Assert-Dotnet
dotnet test tests/CursedPasswordManager.Tests -c Release --no-build --no-restore --logger 'trx;LogFileName=release.trx' --results-directory .build/TestResults
Assert-Dotnet
$uiOutput = Join-Path $root ('.build/windows-validation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $uiOutput -Force | Out-Null
$testExe = Join-Path $root 'tests/CursedPasswordManager.WindowsTests/bin/Release/net10.0-windows/CursedPasswordManager.WindowsTests.exe'
$testStart = [Diagnostics.ProcessStartInfo]::new($testExe, ('"' + $uiOutput + '"'))
$testStart.UseShellExecute = $false
$testStart.CreateNoWindow = $true
$testStart.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$testStart.RedirectStandardOutput = $true
$testStart.RedirectStandardError = $true
$testProcess = [Diagnostics.Process]::Start($testStart)
$standardOutput = $testProcess.StandardOutput.ReadToEndAsync()
$standardError = $testProcess.StandardError.ReadToEndAsync()
if (-not $testProcess.WaitForExit(180000)) { Stop-Process -Id $testProcess.Id; throw 'O teste WPF excedeu 180 segundos.' }
[IO.File]::WriteAllText((Join-Path $uiOutput 'stdout.txt'), $standardOutput.GetAwaiter().GetResult())
[IO.File]::WriteAllText((Join-Path $uiOutput 'stderr.txt'), $standardError.GetAwaiter().GetResult())
Get-Content -LiteralPath (Join-Path $uiOutput 'stdout.txt')
if ($testProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $uiOutput 'result.txt'))) {
    Get-Content -LiteralPath (Join-Path $uiOutput 'stderr.txt')
    throw 'Falha nos testes de integracao Windows.'
}
New-Item -ItemType Directory -Path assets/screenshots -Force | Out-Null
Copy-Item -Path (Join-Path $uiOutput '*.png') -Destination assets/screenshots -Force
$extendedOutput = Join-Path $root ('.build/extended-validation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extendedOutput -Force | Out-Null
$testStart.Arguments = '--extended "' + $extendedOutput + '"'
$testProcess = [Diagnostics.Process]::Start($testStart)
$standardOutput = $testProcess.StandardOutput.ReadToEndAsync()
$standardError = $testProcess.StandardError.ReadToEndAsync()
if (-not $testProcess.WaitForExit(180000)) { Stop-Process -Id $testProcess.Id; throw 'Os testes Windows adicionais excederam 180 segundos.' }
[IO.File]::WriteAllText((Join-Path $extendedOutput 'stdout.txt'), $standardOutput.GetAwaiter().GetResult())
[IO.File]::WriteAllText((Join-Path $extendedOutput 'stderr.txt'), $standardError.GetAwaiter().GetResult())
Get-Content -LiteralPath (Join-Path $extendedOutput 'stdout.txt')
if ($testProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $extendedOutput 'result.txt'))) {
    Get-Content -LiteralPath (Join-Path $extendedOutput 'stderr.txt')
    throw 'Falha nos testes Windows adicionais.'
}
$staging = Join-Path $root ('.build/release-' + [Guid]::NewGuid().ToString('N'))
$normalOutput = Join-Path $staging 'windows-x64'
$portableOutput = Join-Path $staging 'portable-x64'
$app = 'src/CursedPasswordManager.App/CursedPasswordManager.App.csproj'
dotnet publish $app -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=packages.singlefile.lock.json -p:RestoreLockedMode=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $normalOutput
Assert-Dotnet
dotnet publish $app -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=packages.portable.lock.json -p:RestoreLockedMode=true -p:PublishSingleFile=false -p:DebugType=None -o $portableOutput
Assert-Dotnet
foreach ($destination in @($normalOutput,$portableOutput)) {
    Copy-Item -LiteralPath README.md,THIRD-PARTY-NOTICES.md -Destination $destination -Force
    Copy-Item -LiteralPath docs -Destination $destination -Recurse -Force
    Copy-Item -LiteralPath assets -Destination $destination -Recurse -Force
    $runtimeLicense = Get-ChildItem -Path (Join-Path $env:NUGET_PACKAGES 'microsoft.netcore.app.runtime.win-x64/*/LICENSE.TXT') | Sort-Object FullName -Descending | Select-Object -First 1
    if ($runtimeLicense) { Copy-Item -LiteralPath $runtimeLicense.FullName -Destination (Join-Path $destination 'docs/third-party/DotNet-MIT.txt') -Force }
    $runtimeNotices = Get-ChildItem -Path (Join-Path $env:NUGET_PACKAGES 'microsoft.netcore.app.runtime.win-x64/*/THIRD-PARTY-NOTICES.TXT') | Sort-Object FullName -Descending | Select-Object -First 1
    if ($runtimeNotices) { Copy-Item -LiteralPath $runtimeNotices.FullName -Destination (Join-Path $destination 'docs/third-party/DotNet-Notices.txt') -Force }
    $desktopLicense = Get-ChildItem -Path (Join-Path $env:NUGET_PACKAGES 'microsoft.windowsdesktop.app.runtime.win-x64/*/LICENSE') | Sort-Object FullName -Descending | Select-Object -First 1
    if ($desktopLicense) { Copy-Item -LiteralPath $desktopLicense.FullName -Destination (Join-Path $destination 'docs/third-party/WindowsDesktop-License.txt') -Force }
}
[IO.File]::WriteAllText((Join-Path $portableOutput 'portable.flag'), 'Portable mode: keep this file beside the executable.')
New-Item -ItemType Directory -Path dist/windows-x64,dist/portable-x64 -Force | Out-Null
# Package only pristine staging outputs, never a previously used portable Data directory.
Compress-Archive -Path (Join-Path $normalOutput '*') -DestinationPath dist/CursedPasswordManager-win-x64.zip -Force
Compress-Archive -Path (Join-Path $portableOutput '*') -DestinationPath dist/CursedPasswordManager-portable-x64.zip -Force
Copy-Item -Path (Join-Path $normalOutput '*') -Destination dist/windows-x64 -Recurse -Force
Copy-Item -Path (Join-Path $portableOutput '*') -Destination dist/portable-x64 -Recurse -Force
$hashLines = foreach ($artifact in @('dist/windows-x64/CursedPasswordManager.exe','dist/portable-x64/CursedPasswordManager.exe','dist/CursedPasswordManager-win-x64.zip','dist/CursedPasswordManager-portable-x64.zip')) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead((Join-Path $root $artifact))
    try { ([BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()) + '  ' + $artifact }
    finally { $stream.Dispose(); $sha.Dispose() }
}
[IO.File]::WriteAllLines((Join-Path $root 'dist/SHA256SUMS.txt'), [string[]]$hashLines)
$hashLines | ForEach-Object { Write-Host $_ }
Write-Host 'Build, testes e publicacao concluidos.'
