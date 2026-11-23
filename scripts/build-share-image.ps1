param(
	[Parameter(Mandatory)]
	[string] $ImagePath,

	[Parameter(Mandatory)]
	[ValidateSet("Header", "Cover")]
	[string] $Kind,

	[ValidateSet("Top", "Center", "Bottom")]
	[string] $Focus = "Top"
)

$ErrorActionPreference = "Stop"
$Source = Get-Item -LiteralPath $ImagePath
if ($Source -isnot [System.IO.FileInfo] -or $Source.Name -notmatch '^\d+\.(avif|jpe?g|png|webp)$') {
	throw "Choose a revision image such as 200.avif from the site's images directory."
}
$Destination = "$($Source.FullName).$($Kind.ToLowerInvariant())-social-v1.jpg"
if ([System.IO.File]::Exists($Destination)) {
	throw "A share image already exists at $Destination."
}

$TemporaryImage = Join-Path $Source.DirectoryName "$([Guid]::NewGuid()).jpg"
$Size = "1200x630"
try {
	if ($Kind -eq "Cover") {
		& magick $Source.FullName -auto-orient -resize "${Size}>" -background "#181c22" -gravity Center -extent $Size -alpha remove -alpha off -strip -quality 86 $TemporaryImage
	}
	else {
		$Gravity = @{ Top = "North"; Center = "Center"; Bottom = "South" }[$Focus]
		& magick $Source.FullName -auto-orient -resize "${Size}^" -background "#181c22" -gravity $Gravity -extent $Size -alpha remove -alpha off -strip -quality 86 $TemporaryImage
	}
	if ($LASTEXITCODE -ne 0) {
		throw "ImageMagick failed to prepare the share image."
	}
	[System.IO.File]::Move($TemporaryImage, $Destination)
	Write-Output $Destination
}
finally {
	if ([System.IO.File]::Exists($TemporaryImage)) {
		Remove-Item -LiteralPath $TemporaryImage
	}
}
