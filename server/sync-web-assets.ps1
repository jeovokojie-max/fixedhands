$ErrorActionPreference = 'Stop'
$webRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = $PSScriptRoot
$projectImages = Join-Path $projectRoot 'images'
New-Item -ItemType Directory -Path $projectImages -Force | Out-Null
foreach ($file in @('index.html', 'styles.css', 'app.js', 'api-config.js', 'manifest.webmanifest', 'icon.svg', 'service-worker.js')) {
    Copy-Item -LiteralPath (Join-Path $webRoot $file) -Destination (Join-Path $projectRoot $file) -Force
}
Copy-Item -LiteralPath (Join-Path $webRoot 'images/fixhands-flyer.png') -Destination (Join-Path $projectImages 'fixhands-flyer.png') -Force
Write-Output "Fixhands app assets synced to $projectRoot"
