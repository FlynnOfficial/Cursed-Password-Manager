$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$fixture = Join-Path $root ('.build/published-verification-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$executables = foreach ($distribution in @('windows-x64','portable-x64')) {
    $destination = Join-Path $fixture $distribution
    Copy-Item -LiteralPath (Join-Path $root ('dist/' + $distribution)) -Destination $destination -Recurse
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $destination 'CursedPasswordManager.exe'), '--portable')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process = [Diagnostics.Process]::Start($start)
    try {
        $limit = [DateTime]::UtcNow.AddSeconds(25)
        do {
            Start-Sleep -Milliseconds 200
            $process.Refresh()
            if ($process.HasExited) { throw ('Executable exited early: ' + $distribution) }
        } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $limit)
        if ($process.MainWindowHandle -eq [IntPtr]::Zero -or $process.MainWindowTitle -ne 'Cursed Password Manager') { throw ('Expected application window missing: ' + $distribution) }
        if (-not (Test-Path -LiteralPath (Join-Path $destination 'Data'))) { throw 'Portable fixture root was not created.' }
        [pscustomobject]@{ Distribution=$distribution; Running=$true; WindowTitle=$process.MainWindowTitle; WindowPresent=$true; DataRootCreated=$true }
    }
    finally { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archives = foreach ($name in @('CursedPasswordManager-win-x64.zip','CursedPasswordManager-portable-x64.zip')) {
    $path = Join-Path $root ('dist/' + $name)
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $unexpected = @($zip.Entries | Where-Object { $_.FullName -match '(?i)(\.cpmvault|\.cpmbackup|(^|[/\\])settings\.json|(^|[/\\])device-unlock\.json|(^|[/\\])Data[/\\])' })
        if ($unexpected.Count -ne 0) { throw ('Private/test data included in ' + $name) }
        foreach ($document in @('README.md','docs/validation.md','docs/security.md','docs/build.md')) {
            $entry = $zip.GetEntry($document)
            if ($null -eq $entry) { $entry = $zip.GetEntry($document.Replace('/','\')) }
            if ($null -eq $entry) { throw ('Missing packaged document: ' + $document) }
            $reader = [IO.StreamReader]::new($entry.Open())
            try { if ($reader.ReadToEnd() -ne [IO.File]::ReadAllText((Join-Path $root $document))) { throw ('Stale packaged document: ' + $document) } }
            finally { $reader.Dispose() }
        }
        [pscustomobject]@{ Archive=$name; Entries=$zip.Entries.Count; ConfidentialDataFiles=$unexpected.Count; Bytes=(Get-Item -LiteralPath $path).Length }
    }
    finally { $zip.Dispose() }
}
foreach ($line in [IO.File]::ReadAllLines((Join-Path $root 'dist/SHA256SUMS.txt'))) {
    $parts = $line -split '  ',2
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead((Join-Path $root $parts[1]))
    try {
        $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant()
        if ($actual -ne $parts[0]) { throw ('Artifact hash mismatch: ' + $parts[1]) }
    }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$result = [pscustomobject]@{ Executables=@($executables); Archives=@($archives); HashesVerified=$true; CheckedAt=[DateTimeOffset]::Now.ToString('O') }
$json = $result | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $root '.build/final-package-verification.json'), $json)
$json
