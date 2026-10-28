$ErrorActionPreference = 'Stop'
$webRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $PSScriptRoot 'app/src/main/assets'
$imageRoot = Join-Path $assetRoot 'images'
New-Item -ItemType Directory -Path $imageRoot -Force | Out-Null
foreach ($file in @('index.html', 'styles.css', 'app.js', 'api-config.js', 'manifest.webmanifest', 'icon.svg', 'service-worker.js')) {
    Copy-Item -LiteralPath (Join-Path $webRoot $file) -Destination (Join-Path $assetRoot $file) -Force
}
Copy-Item -LiteralPath (Join-Path $webRoot 'images/fixhands-flyer.png') -Destination (Join-Path $imageRoot 'fixhands-flyer.png') -Force
Write-Output "Fixhands web assets synced to $assetRoot"
