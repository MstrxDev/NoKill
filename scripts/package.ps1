<#
.SYNOPSIS
  Builds the signed NoKill installer.

.DESCRIPTION
  1. Publishes NoKill.App and NoKill.Cli (self-contained win-x64) into one
     shared folder — users need no .NET runtime installed.
  2. Signs both executables.
  3. Builds the MSI with WiX (repo-local dotnet tool).
  4. Signs the MSI.

  Signing certificate resolution, in order:
    - $env:NOKILL_CERT_THUMBPRINT — a real code-signing cert in the current
      user's store (production: Azure Trusted Signing / an OV certificate).
    - otherwise a self-signed "NoKill Dev Signing" cert is created/reused.
      Dev signatures are structurally valid but untrusted on other machines;
      they prove the pipeline, not identity.

.PARAMETER SkipSigning
  Build the installer without signing anything.
#>
param(
    [switch]$SkipSigning
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repo 'artifacts'
$publishDir = Join-Path $artifacts 'publish'

# --- version from the single source of truth ---
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "Packaging NoKill $version" -ForegroundColor Cyan

# --- publish (both apps share one folder; identical runtime files coexist) ---
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
foreach ($project in 'src\NoKill.App', 'src\NoKill.Cli') {
    Write-Host "Publishing $project..." -ForegroundColor Cyan
    dotnet publish (Join-Path $repo $project) -c Release -r win-x64 --self-contained true `
        -o $publishDir -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $project" }
}

# --- signing setup ---
function Get-SignTool {
    $candidates = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending
    if ($candidates) { return $candidates[0].FullName }
    return $null
}

function Get-SigningCert {
    if ($env:NOKILL_CERT_THUMBPRINT) {
        $cert = Get-Item "Cert:\CurrentUser\My\$($env:NOKILL_CERT_THUMBPRINT)" -ErrorAction Stop
        Write-Host "Using certificate from NOKILL_CERT_THUMBPRINT: $($cert.Subject)" -ForegroundColor Cyan
        return $cert
    }

    $existing = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object Subject -eq 'CN=NoKill Dev Signing' |
        Where-Object NotAfter -gt (Get-Date).AddDays(30) |
        Select-Object -First 1
    if ($existing) { return $existing }

    Write-Host 'Creating self-signed dev signing certificate (CN=NoKill Dev Signing)...' -ForegroundColor Yellow
    return New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=NoKill Dev Signing' `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(3)
}

function Sign-File([string]$path, $cert, [string]$signtool) {
    if ($signtool) {
        & $signtool sign /sha1 $cert.Thumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $path 2>$null
        if ($LASTEXITCODE -eq 0) { return $true }
        # timestamp server unreachable or other transient issue: sign without timestamp
        & $signtool sign /sha1 $cert.Thumbprint /fd SHA256 $path 2>$null
        return ($LASTEXITCODE -eq 0)
    }

    # Fallback without the Windows SDK: PE files only (cannot sign the MSI)
    if ($path.EndsWith('.msi')) { return $false }
    $result = Set-AuthenticodeSignature -FilePath $path -Certificate $cert -HashAlgorithm SHA256 `
        -TimestampServer 'http://timestamp.digicert.com' -ErrorAction SilentlyContinue
    return ($null -ne $result.SignerCertificate)
}

$cert = $null
$signtool = $null
if (-not $SkipSigning) {
    $signtool = Get-SignTool
    $cert = Get-SigningCert
    if (-not $signtool) {
        Write-Warning 'signtool.exe not found (Windows SDK); using Set-AuthenticodeSignature — the MSI itself will not be signed.'
    }

    foreach ($exe in 'NoKill.App.exe', 'NoKill.Cli.exe') {
        $path = Join-Path $publishDir $exe
        if (Sign-File $path $cert $signtool) {
            Write-Host "Signed $exe" -ForegroundColor Green
        } else {
            Write-Warning "Could not sign $exe"
        }
    }
}

# --- MSI ---
Write-Host 'Building MSI...' -ForegroundColor Cyan
$msi = Join-Path $artifacts "NoKill-$version-win-x64.msi"
Push-Location $repo
try {
    dotnet tool run wix build (Join-Path $repo 'installer\NoKill.wxs') `
        -d "Version=$version" -d "PublishDir=$publishDir" `
        -arch x64 -o $msi
    if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }
}
finally {
    Pop-Location
}

if (-not $SkipSigning -and $cert) {
    if (Sign-File $msi $cert $signtool) {
        Write-Host 'Signed MSI' -ForegroundColor Green
    } else {
        Write-Warning 'MSI left unsigned (signtool required to sign MSI files).'
    }
}

$size = [math]::Round((Get-Item $msi).Length / 1MB, 1)
Write-Host "`nDone: $msi ($size MB)" -ForegroundColor Green
if (-not $SkipSigning -and $cert -and $cert.Subject -eq 'CN=NoKill Dev Signing') {
    Write-Host 'NOTE: signed with the self-signed DEV certificate. For distribution, set' -ForegroundColor Yellow
    Write-Host '      NOKILL_CERT_THUMBPRINT to a trusted code-signing certificate.' -ForegroundColor Yellow
}
