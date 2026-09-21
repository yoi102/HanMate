#requires -Version 7.2
param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Python = 'python',
    [string]$AndroidSdk = 'C:/Program Files (x86)/Android/android-sdk',
    [string]$BuildToolsVersion = '36.0.0'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Use a new output directory.' }
$androidTools = Join-Path $AndroidSdk "build-tools/$BuildToolsVersion"
foreach ($tool in @('aapt.exe', 'apksigner.bat')) {
    if (-not (Test-Path -LiteralPath (Join-Path $androidTools $tool))) { throw "Missing Android tool: $tool" }
}
Push-Location $repo
try {
    $changes = & git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit or resolve working-tree changes before preparing a candidate.' }
    $sourceCommit = & git rev-parse HEAD
    [xml]$project = Get-Content HanMate.App/HanMate.App.csproj -Raw
    $version = $project.SelectSingleNode('//ApplicationDisplayVersion').InnerText
    $buildNumber = $project.SelectSingleNode('//ApplicationVersion').InnerText
    $applicationId = $project.SelectSingleNode('//ApplicationId').InnerText
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a numeric three-part app version.' }
    $runId = 'candidate-' + [Guid]::NewGuid().ToString('N')
    $buildRoot = Join-Path $destination 'build'
    $assets = Join-Path $destination 'assets'
    New-Item -ItemType Directory -Path $buildRoot, $assets | Out-Null
    function Invoke-Recorded([string]$Name, [string[]]$Arguments) {
        & dotnet @Arguments *> (Join-Path $buildRoot "$Name.log")
        if ($LASTEXITCODE -ne 0) { throw "$Name failed; inspect build/$Name.log" }
        Write-Output "$Name completed"
    }
    Invoke-Recorded 'restore-locked' @('restore', 'HanMate.slnx', '--locked-mode')
    $windows = Join-Path $buildRoot 'windows'
    Invoke-Recorded 'windows-publish' @('publish', 'HanMate.App/HanMate.App.csproj', '-c', 'Release', '-f', 'net10.0-windows10.0.19041.0', '--no-restore', '-p:RuntimeIdentifierOverride=win-x64', '-p:WindowsAppSDKSelfContained=true', '--self-contained', 'true', "-p:IntermediateOutputPath=obj/$runId/windows/", "-p:OutputPath=$buildRoot/windows-build/", "-p:PublishDir=$windows/", '-v', 'minimal')
    # Build one ABI so global isolated intermediate paths cannot collide across ABIs.
    Invoke-Recorded 'android-build' @('build', 'HanMate.App/HanMate.App.csproj', '-c', 'Release', '-f', 'net10.0-android', '-r', 'android-arm64', '--no-restore', "-p:IntermediateOutputPath=obj/$runId/android/", "-p:OutputPath=$buildRoot/android/", '-v', 'minimal')
    $apk = Join-Path $buildRoot "android/$applicationId-Signed.apk"
    & (Join-Path $androidTools 'apksigner.bat') verify --print-certs $apk *> (Join-Path $buildRoot 'android-signature.log')
    if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
    $badging = & (Join-Path $androidTools 'aapt.exe') dump badging $apk
    if ($LASTEXITCODE -ne 0) { throw 'APK metadata inspection failed.' }
    $badging | Set-Content -LiteralPath (Join-Path $buildRoot 'android-metadata.log') -Encoding utf8
    if (($badging -join "`n") -notmatch "versionCode='$buildNumber' versionName='$([regex]::Escape($version))'") { throw 'APK version mismatch.' }
    foreach ($file in @('HanMate.App.exe', 'coreclr.dll', 'Microsoft.UI.Xaml.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $windows $file))) { throw "Windows self-contained output missing: $file" }
    }
    $windowsVersion = (Get-Item -LiteralPath (Join-Path $windows 'HanMate.App.exe')).VersionInfo.ProductVersion
    if ($windowsVersion -notlike "$version*") { throw 'Windows version mismatch.' }
    & $Python -X utf8 tools/release_preflight.py --apk $apk --aapt (Join-Path $androidTools 'aapt.exe') --output (Join-Path $assets 'release-preflight.json') *> (Join-Path $buildRoot 'release-preflight.log')
    if ($LASTEXITCODE -notin @(0, 2)) { throw 'Release preflight could not finish.' }
    $preflight = Get-Content (Join-Path $assets 'release-preflight.json') -Raw | ConvertFrom-Json
    $changes = & git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $changes -or (& git rev-parse HEAD) -ne $sourceCommit) { throw 'Source changed while building; prepare again from a stable commit.' }
    Copy-Item -LiteralPath $apk -Destination (Join-Path $assets "HanMate-$version-android-arm64-dev-signed.apk")
    Copy-Item -LiteralPath "docs/releases/$version.md" -Destination (Join-Path $assets 'RELEASE-NOTES.md')
    $zip = Join-Path $assets "HanMate-$version-windows-x64-preview.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($windows, $zip, [IO.Compression.CompressionLevel]::Fastest, $false)
    # Read every archived file and compare its hash to the actual publish output.
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $publishedCount = @(Get-ChildItem -LiteralPath $windows -File -Recurse).Count
        $fileEntries = @($archive.Entries | Where-Object { $_.Name })
        if ($fileEntries.Count -ne $publishedCount) { throw 'Windows archive file count mismatch.' }
        foreach ($entry in $fileEntries) {
            $inputFile = Join-Path $windows $entry.FullName
            $stream = $entry.Open()
            try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($digest -ne (Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash) { throw "ZIP verification failed: $($entry.FullName)" }
        }
    } finally { $archive.Dispose() }
    $files = @(Get-ChildItem -LiteralPath $assets -File | Sort-Object Name | ForEach-Object {
        [ordered]@{ name = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    [ordered]@{
        version = $version; buildNumber = $buildNumber; sourceCommit = $sourceCommit; preparedAt = (Get-Date -Format o)
        applicationId = $applicationId; channel = 'local-preview-candidate'; published = $false
        windows = 'Release, win-x64, self-contained, unpackaged, unsigned'
        android = 'Release, ARM64, existing developer signing key; certificate in build/android-signature.log'
        packaging = 'PASS: APK signature/version, Windows runtime/version, every ZIP entry hash'
        deviceValidation = 'NOT RUN'; automatedTests = 'Separate evidence required'
        publicReleaseReady = $preflight.releaseReady; publicReleaseBlockedChecks = $preflight.blocked; files = $files
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $assets 'manifest.json') -Encoding utf8
    Get-ChildItem -LiteralPath $assets -File | Sort-Object Name | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
    } | Set-Content -LiteralPath (Join-Path $assets 'SHA256SUMS.txt') -Encoding utf8
    Write-Output "Prepared local candidate: $assets"
    Write-Output "Public release ready: $($preflight.releaseReady). No installation, tag creation or publication performed."
} finally { Pop-Location }
