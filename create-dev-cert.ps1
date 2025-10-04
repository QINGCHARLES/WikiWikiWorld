# PowerShell script to create development certificates for localhost subdomains
# Run this script as Administrator

$ErrorActionPreference = "Continue"
$logFile = "$PSScriptRoot\cert-creation.log"

function Write-Log {
    param($Message, $Color = "White")
    Write-Host $Message -ForegroundColor $Color
    Add-Content -Path $logFile -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $Message"
}

Write-Log "Starting certificate creation process..." "Green"
Write-Log "Log file: $logFile" "Gray"

# Define the subdomains you need
$subdomains = @("en.localhost", "fr.localhost", "fanfare.localhost", "crtr.localhost")

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

$successCount = 0
foreach ($subdomain in $subdomains) {
    Write-Log "`nCreating certificate for $subdomain..." "Cyan"
    
    try {
        # Create a self-signed certificate for each subdomain
        $cert = New-SelfSignedCertificate `
            -Subject "CN=$subdomain" `
            -DnsName $subdomain `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -NotBefore (Get-Date) `
            -NotAfter (Get-Date).AddYears(5) `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -FriendlyName "WikiWikiWorld Dev - $subdomain" `
            -HashAlgorithm SHA256 `
            -KeyUsage DigitalSignature, KeyEncipherment, DataEncipherment `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
            -ErrorAction Stop
        
        Write-Log "  Certificate created with thumbprint: $($cert.Thumbprint)" "Gray"
        
        # Export and import to Trusted Root
        $tempPfx = "$env:TEMP\wikiwikiworld_$($subdomain.Replace('.', '_')).pfx"
        $certPassword = ConvertTo-SecureString -String "temp-password-123" -Force -AsPlainText
        
        Write-Log "  Exporting certificate..." "Gray"
        Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $tempPfx -Password $certPassword -ErrorAction Stop | Out-Null
        
        Write-Log "  Importing to Trusted Root..." "Gray"
        Import-PfxCertificate -FilePath $tempPfx -CertStoreLocation Cert:\CurrentUser\Root -Password $certPassword -Exportable -ErrorAction Stop | Out-Null
        
        if (Test-Path $tempPfx) {
            Remove-Item $tempPfx -Force -ErrorAction SilentlyContinue
        }
        
        Write-Log "  ✓ Certificate for $subdomain created and trusted successfully" "Green"
        $successCount++
        
    } catch {
        Write-Log "  ✗ ERROR creating certificate for $subdomain`: $_" "Red"
        Write-Log "  Stack trace: $($_.ScriptStackTrace)" "Red"
    }
}

Write-Log "`n========================================" "Green"
Write-Log "Process completed: $successCount of $($subdomains.Count) certificates created successfully" "Green"
Write-Log "========================================" "Green"

if ($successCount -gt 0) {
    Write-Log @"

✓ Development certificates created for:
$(($subdomains[0..($successCount-1)] | ForEach-Object { "  - $_" }) -join "`n")

Next steps:
  1. Close your browser completely (quit Chrome/Edge entirely)
  2. Restart the VS Code debugger
  3. The browser should now trust https://en.localhost:7126

To remove these certificates later:
  - Run: certmgr.msc
  - Delete certificates with "WikiWikiWorld Dev" in the name from both:
    * Personal > Certificates
    * Trusted Root Certification Authorities > Certificates

"@ "Green"
} else {
    Write-Log "`nNo certificates were created. Check the log file for errors: $logFile" "Red"
}

Write-Log "`nPress Enter to close this window..." "Yellow"
Read-Host

Write-Host "Certificate created with thumbprint: $($cert.Thumbprint)" -ForegroundColor Yellow

# Export the certificate to a PFX file
$certPasswordSecure = ConvertTo-SecureString -String $certPassword -Force -AsPlainText
Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $certPath -Password $certPasswordSecure | Out-Null

Write-Host "Certificate exported to: $certPath" -ForegroundColor Yellow

# Import the certificate to Trusted Root Certification Authorities
Write-Host "Installing certificate to Trusted Root..." -ForegroundColor Green
Import-PfxCertificate -FilePath $certPath -CertStoreLocation Cert:\CurrentUser\Root -Password $certPasswordSecure -Exportable | Out-Null

Write-Host @"

✓ Development certificate created successfully!

The certificate is now trusted for:
  - localhost
  - *.localhost (including en.localhost, fr.localhost, etc.)

You can now run your application with HTTPS on subdomain URLs.

To remove this certificate later:
  1. Open 'certmgr.msc'
  2. Navigate to Personal > Certificates
  3. Delete the certificate with friendly name 'ASP.NET Core Development Certificate (localhost subdomains)'
  4. Navigate to Trusted Root Certification Authorities > Certificates
  5. Delete the same certificate there

"@ -ForegroundColor Green

# Clean up the PFX file
Remove-Item $certPath -Force -ErrorAction SilentlyContinue
