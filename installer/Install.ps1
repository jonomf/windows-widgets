# Installs Jono's Widgets: trusts its signing certificate (once, needs admin), installs the
# Windows App Runtime it needs, then the app. Safe to run again to update.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$cer = Join-Path $here 'jono-widgets.cer'

# 1. Trust the certificate the package is signed with (only the first time).
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $cer
if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)")) {
    Write-Host "Trusting the app's signing certificate. Windows will ask for permission..."
    $cmd = "Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-Command', $cmd
    if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)")) {
        throw 'The certificate was not trusted, so Windows will refuse to install the app.'
    }
}

# 2. The Windows App Runtime (skipped where the same or newer is already installed).
$deps = Get-ChildItem (Join-Path $here 'Dependencies') -Filter *.msix -ErrorAction SilentlyContinue
foreach ($d in $deps) {
    try {
        Add-AppxPackage -Path $d.FullName -ErrorAction Stop
        Write-Host "Installed $($d.Name)"
    } catch {
        Write-Host "Skipped $($d.Name) (already installed)"
    }
}

# 3. The app itself.
Add-AppxPackage -Path (Join-Path $here 'JonoWidgets.msix') -ForceApplicationShutdown
Write-Host ''
Write-Host "Done. Press Win+W, click + (Add widgets), and pin Todoist, Repos or Now from Jono's Widgets."
