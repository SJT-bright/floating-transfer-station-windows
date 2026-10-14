[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$release = Join-Path $repoRoot 'artifacts/release'
New-Item -ItemType Directory -Force $release | Out-Null
$version = ([xml](Get-Content (Join-Path $repoRoot 'src/FloatingTransferStation/FloatingTransferStation.csproj'))).Project.PropertyGroup.Version
$installer = @(Get-ChildItem (Join-Path $repoRoot 'artifacts/installer') -Filter '*-Setup-*.exe')
if ($installer.Count -ne 1) { throw 'Expected one installer.' }
Copy-Item $installer[0].FullName (Join-Path $release "FloatingTransferStation-Setup-$version.exe")
Compress-Archive -Path (Join-Path $repoRoot 'artifacts/publish/*') -DestinationPath (Join-Path $release "FloatingTransferStation-Windows-x64-$version.zip") -Force

# Only run this probe on the disposable Windows CI machine, never on a developer's desktop.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Installer probe requires disposable GitHub Actions environment.' }
$install = Start-Process $installer[0].FullName -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -PassThru -Wait
if ($install.ExitCode -ne 0) { throw "Installer exit code: $($install.ExitCode)" }
$app = Join-Path $env:LOCALAPPDATA 'Programs/悬浮中转站/悬浮中转站.exe'
if (!(Test-Path -LiteralPath $app)) { throw 'Installed executable is missing.' }
$process = Start-Process -FilePath $app -PassThru
try {
    Start-Sleep -Seconds 5
    $process.Refresh()
    if ($process.HasExited) { throw "App exited during launch: $($process.ExitCode)" }
    if (!$process.Responding) { throw 'App is not responding.' }
    $startup = Get-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
    if ($startup.'悬浮中转站' -notlike '*悬浮中转站.exe*') { throw 'Login startup registration is missing.' }
    if (!(Test-Path -LiteralPath (Join-Path (Split-Path $app) 'licenses/SkiaNative-NOTICES.txt'))) { throw 'Native codec notices are missing from installed package.' }
    $oldPid = $process.Id
    $helper = Start-Process -FilePath $app -ArgumentList '--restart','--target-pid',"$oldPid" -PassThru
    if (!$helper.WaitForExit(15000)) { throw 'Restart helper did not finish.' }
    if (!$process.WaitForExit(5000)) { throw 'Original process was not restarted.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    $replacement = $null
    do {
        $replacement = @(Get-Process | Where-Object { $_.Id -ne $oldPid -and $_.Id -ne $helper.Id -and $_.Path -eq $app }) | Select-Object -First 1
        if (!$replacement) { Start-Sleep -Milliseconds 200 }
    } while (!$replacement -and [DateTime]::UtcNow -lt $deadline)
    if (!$replacement) { throw 'Replacement application did not launch.' }
    $process = $replacement
    Start-Sleep -Seconds 2
    $process.Refresh()
    if ($process.HasExited -or !$process.Responding) { throw 'Replacement application is not responding.' }
    "Installed self-contained app launched and is responding. PID=$($process.Id). Startup registered." |
        Set-Content (Join-Path $repoRoot 'artifacts/evidence/install-smoke.txt')
    "Independent --restart helper replaced PID $oldPid with PID $($process.Id). Replacement responds." |
        Set-Content (Join-Path $repoRoot 'artifacts/evidence/restart-smoke.txt')
} finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id }
}
Get-ChildItem $release -File | Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash.ToLower())  $([IO.Path]::GetFileName($_.Path))" } |
    Set-Content (Join-Path $release 'SHA256SUMS.txt')
