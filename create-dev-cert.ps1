# PowerShell script to create a multi-domain development certificate for *.localhost sites
# Run this script as Administrator if needed to trust the certificate in CurrentUser\Root

$ErrorActionPreference = "Continue"
$logFile = "$PSScriptRoot\cert-creation.log"

function Write-Log {
    param($Message, $Color = "White")
    Write-Host $Message -ForegroundColor $Color
    Add-Content -Path $logFile -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $Message"
}

Write-Log "Starting certificate creation process..." "Green"
Write-Log "Log file: $logFile" "Gray"

# Primary subject matches ApplicationStartup:DevelopmentHttpsCertificateSubjectName in appsettings.Development.json
$primarySubject = "en.magazedia.com.localhost"

# All local development hostnames covered by Subject Alternative Names (SANs)
$dnsNames = @(
    "en.magazedia.com.localhost",
    "magazedia.com.localhost",
    "ja.magazedia.com.localhost",
    "ar.magazedia.com.localhost",
    "xx-test.magazedia.com.localhost",
    "cinepedia.wiki.localhost",
    "en.cinepedia.wiki.localhost",
    "fanfare.wiki.localhost",
    "crtr.world.localhost",
    "localhost",
    "en.localhost",
    "ja.localhost",
    "ar.localhost",
    "xx-test.localhost"
)

# First, remove any existing certificates with our friendly name
Write-Log "Cleaning up old certificates..." "Yellow"
try {
    Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -like "WikiWikiWorld Dev*" } | ForEach-Object {
        Write-Log "  Removing old cert from My: $($_.Subject)" "Gray"
        Remove-Item $_.PSPath -ErrorAction SilentlyContinue
    }
    Get-ChildItem Cert:\CurrentUser\Root -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -like "WikiWikiWorld Dev*" } | ForEach-Object {
        Write-Log "  Removing old cert from Root: $($_.Subject)" "Gray"
        Remove-Item $_.PSPath -ErrorAction SilentlyContinue
    }
} catch {
    Write-Log "  Warning during cleanup: $_" "Yellow"
}

Write-Log "`nCreating multi-domain certificate (Subject: CN=$primarySubject)..." "Cyan"
$success = $false

try {
    $cert = New-SelfSignedCertificate `
        -Subject "CN=$primarySubject" `
        -DnsName $dnsNames `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -NotBefore (Get-Date) `
        -NotAfter (Get-Date).AddYears(5) `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -FriendlyName "WikiWikiWorld Dev - Multi-Site ($primarySubject)" `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature, KeyEncipherment, DataEncipherment `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
        -ErrorAction Stop

    Write-Log "  Certificate created with thumbprint: $($cert.Thumbprint)" "Gray"

    # Export and import to Trusted Root
    $tempPfx = "$env:TEMP\wikiwikiworld_dev_multisite.pfx"
    $certPassword = ConvertTo-SecureString -String "temp-password-123" -Force -AsPlainText

    Write-Log "  Exporting certificate..." "Gray"
    Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $tempPfx -Password $certPassword -ErrorAction Stop | Out-Null

    Write-Log "  Importing to Trusted Root..." "Gray"
    Import-PfxCertificate -FilePath $tempPfx -CertStoreLocation Cert:\CurrentUser\Root -Password $certPassword -Exportable -ErrorAction Stop | Out-Null

    if (Test-Path $tempPfx) {
        Remove-Item $tempPfx -Force -ErrorAction SilentlyContinue
    }

    Write-Log "  ✓ Multi-domain development certificate created and trusted successfully" "Green"
    $success = $true
} catch {
    Write-Log "  ✗ ERROR creating certificate: $_" "Red"
    Write-Log "  Stack trace: $($_.ScriptStackTrace)" "Red"
}

Write-Log "`n========================================" "Green"
if ($success) {
    Write-Log @"

✓ Development certificate created and trusted for:
$(($dnsNames | ForEach-Object { "  - $_" }) -join "`n")

Next steps:
  1. Close your browser completely (quit Chrome/Edge entirely)
  2. Restart the Visual Studio / VS Code debugger
  3. The browser should now trust:
     - https://en.magazedia.com.localhost:7126
     - https://en.cinepedia.wiki.localhost:7126
     - https://cinepedia.wiki.localhost:7126

To remove this certificate later:
  - Run: certmgr.msc
  - Delete certificates with "WikiWikiWorld Dev" in the name from both:
    * Personal > Certificates
    * Trusted Root Certification Authorities > Certificates

"@ "Green"
} else {
    Write-Log "`nCertificate creation failed. Check the log file for errors: $logFile" "Red"
}
