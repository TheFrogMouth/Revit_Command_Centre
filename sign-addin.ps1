<#
.SYNOPSIS
    Signs Revit_Command_Centre.dll with an Authenticode signature so Revit's
    security dialog shows the correct publisher and issuer.

.DESCRIPTION
    Creates a two-certificate chain the first time it runs:
      Root CA  : "Allan Waller"       (Issuer)
      Code cert: "The Frogmouth"      (Publisher / Subject)

    Both certificates are stored in CurrentUser\My.  Subsequent builds reuse
    the existing certs — they are never regenerated unless you delete them.

    The DLL must be passed as the first argument (MSBuild supplies this
    automatically via the AfterBuild target in the .csproj).

.EXAMPLE
    .\sign-addin.ps1 "C:\...\Revit_Command_Centre.dll"
#>

param(
    [Parameter(Mandatory)][string]$DllPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$rootSubject = "CN=Allan Waller"
$codeSubject = "CN=The Frogmouth"
$friendlyRoot = "BIM Command Centre — Root CA (Allan Waller)"
$friendlyCode = "BIM Command Centre — Code Signing (The Frogmouth)"

function Find-Cert([string]$Subject, [string]$Store = "My") {
    Get-ChildItem "Cert:\CurrentUser\$Store" -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $Subject } |
        Select-Object -First 1
}

# ── 1. Root CA cert (Allan Waller) ────────────────────────────────────────────
$rootCert = Find-Cert $rootSubject
if (-not $rootCert) {
    Write-Host "Creating root CA cert: $rootSubject"
    $rootCert = New-SelfSignedCertificate `
        -Subject        $rootSubject `
        -FriendlyName   $friendlyRoot `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyUsage       CertSign, CRLSign, DigitalSignature `
        -KeyExportPolicy Exportable `
        -NotAfter       (Get-Date).AddYears(20)

    # Also install it as a trusted root so Windows accepts the chain
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
        [System.Security.Cryptography.X509Certificates.StoreName]::Root,
        [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    $store.Open("ReadWrite")
    $store.Add($rootCert)
    $store.Close()
    Write-Host "  → Installed as trusted root (CurrentUser\Root)"
} else {
    Write-Host "Root CA cert already exists: $rootSubject"
}

# ── 2. Code-signing cert (The Frogmouth), issued by Allan Waller ──────────────
$codeCert = Find-Cert $codeSubject
if (-not $codeCert) {
    Write-Host "Creating code-signing cert: $codeSubject"
    $codeCert = New-SelfSignedCertificate `
        -Subject        $codeSubject `
        -FriendlyName   $friendlyCode `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -Type           CodeSigning `
        -Signer         $rootCert `
        -KeyExportPolicy Exportable `
        -NotAfter       (Get-Date).AddYears(10)
    Write-Host "  → Issued by: $($codeCert.Issuer)"
} else {
    Write-Host "Code-signing cert already exists: $codeSubject"
}

# ── 3. Sign the DLL ───────────────────────────────────────────────────────────
if (-not (Test-Path $DllPath)) {
    Write-Error "DLL not found: $DllPath"
    exit 1
}

Write-Host "Signing: $DllPath"
$result = Set-AuthenticodeSignature `
    -FilePath    $DllPath `
    -Certificate $codeCert `
    -HashAlgorithm SHA256

if ($result.Status -ne "Valid") {
    Write-Warning "Signature status: $($result.Status) — $($result.StatusMessage)"
} else {
    Write-Host "  → Signed OK  (Publisher: The Frogmouth  /  Issuer: Allan Waller)"
}
