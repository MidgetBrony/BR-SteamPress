param(
    [string]$Configuration = "Release",
    [string]$GamePath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = "0.3.1"
$dist = Join-Path $projectRoot "dist"
$staging = Join-Path $projectRoot "obj\package\BR-SteamPress-$version"
$dll = Join-Path $projectRoot "bin\$Configuration\netstandard2.1\BR_SteamPress.dll"

$buildArguments = @("build", (Join-Path $projectRoot "BR-SteamPress.csproj"), "-c", $Configuration, "-p:DeployToGame=false")
if (-not [string]::IsNullOrWhiteSpace($GamePath)) {
    $buildArguments += "-p:GamePath=$GamePath"
}
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

New-Item -ItemType Directory -Path $dist -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging "Mods") -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $staging "Mods\BR_SteamPress.dll") -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination (Join-Path $staging "Mods\BR-SteamPress-README.md") -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "THIRD_PARTY_NOTICES.md") -Destination (Join-Path $staging "Mods\BR-SteamPress-THIRD-PARTY-NOTICES.md") -Force

$archive = Join-Path $dist "BR-SteamPress-$version.zip"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $archive -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $dist "SHA256SUMS.txt") -Value "$hash  BR-SteamPress-$version.zip" -Encoding ascii
Write-Host "Created $archive"
