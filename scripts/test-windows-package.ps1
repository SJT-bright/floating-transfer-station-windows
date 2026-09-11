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
    "Installed self-contained app launched and is responding. PID=$($process.Id). Startup registered." |
        Set-Content (Join-Path $repoRoot 'artifacts/evidence/install-smoke.txt')
} finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id }
}
Get-ChildItem $release -File | Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash.ToLower())  $([IO.Path]::GetFileName($_.Path))" } |
    Set-Content (Join-Path $release 'SHA256SUMS.txt')
