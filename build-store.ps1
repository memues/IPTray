[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$IdentityName,
    [Parameter(Mandatory = $true)][string]$Publisher,
    [Parameter(Mandatory = $true)][string]$PublisherDisplayName,
    [string]$DisplayName = 'IPTray by memues',
    [string]$MakeAppxPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'build/store')
)
$ErrorActionPreference = 'Stop'
if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$' -or $Publisher -notmatch '^CN=\S' -or
    [string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
    throw 'Use the package identity and publisher values from Partner Center > Product identity.'
}
if (-not $MakeAppxPath) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $MakeAppxPath = Get-ChildItem -LiteralPath $sdkRoot -Filter makeappx.exe -Recurse |
        Where-Object FullName -Match '[\\/]x64[\\/]makeappx.exe$' |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $MakeAppxPath -or -not (Test-Path -LiteralPath $MakeAppxPath)) {
    throw 'MakeAppx.exe is required. Install the Windows SDK or pass -MakeAppxPath from Microsoft.Windows.SDK.BuildTools.'
}
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src/IPTray/IPTray.csproj')
$version = ($project.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
$packageVersion = "$version.0"
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
# A fresh directory prevents stale desktop helper files from entering a Store package.
$staging = Join-Path (Join-Path $PSScriptRoot 'build/store-staging') ([guid]::NewGuid().ToString('N'))
$appDirectory = Join-Path $staging 'app'
$assetsDirectory = Join-Path $staging 'Assets'
New-Item -ItemType Directory -Path $appDirectory,$assetsDirectory -Force | Out-Null
& dotnet publish (Join-Path $PSScriptRoot 'src/IPTray/IPTray.csproj') -c Release -p:StoreBuild=true -o $appDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'Store build failed.' }

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'packaging/AppxManifest.xml') -Raw
foreach ($replacement in @{
    '@IDENTITY_NAME@' = $IdentityName; '@PUBLISHER@' = $Publisher;
    '@PUBLISHER_DISPLAY_NAME@' = $PublisherDisplayName; '@VERSION@' = $packageVersion
    '@DISPLAY_NAME@' = $DisplayName
}.GetEnumerator()) {
    $manifest = $manifest.Replace($replacement.Key, [System.Security.SecurityElement]::Escape($replacement.Value))
}
Set-Content -LiteralPath (Join-Path $staging 'AppxManifest.xml') -Value $manifest -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE'),(Join-Path $PSScriptRoot 'PRIVACY.md') -Destination $staging

Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'docs/icon.png'))
try {
    foreach ($asset in @{'StoreLogo.png'=50; 'Square44x44Logo.png'=44; 'Square150x150Logo.png'=150}.GetEnumerator()) {
        $bitmap = New-Object System.Drawing.Bitmap($asset.Value, $asset.Value)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $inset = [int]($asset.Value * 0.1)
            $graphics.DrawImage($source, $inset, $inset, $asset.Value - 2 * $inset, $asset.Value - 2 * $inset)
            $bitmap.Save((Join-Path $assetsDirectory $asset.Key), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }

$package = Join-Path $OutputDirectory "IPTray-$version-x64.msix"
& $MakeAppxPath pack /d $staging /p $package /o
if ($LASTEXITCODE -ne 0) { throw 'MSIX validation/packaging failed.' }
Write-Output "Store package: $package"
Write-Output 'Upload this unsigned package to Partner Center. Microsoft signs accepted Store packages.'
