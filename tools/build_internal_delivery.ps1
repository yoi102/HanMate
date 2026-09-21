param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Python = 'python',
    [string]$AndroidSdk = 'C:/Program Files (x86)/Android/android-sdk',
    [string]$JavaSdk = 'C:/Program Files/Android/openjdk/jdk-21.0.8'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Use a new output directory; existing delivery files are never replaced.' }
New-Item -ItemType Directory -Path $destination | Out-Null
$buildRoot = Join-Path $destination 'build'
New-Item -ItemType Directory -Path $buildRoot | Out-Null
function Invoke-Recorded([string]$Name, [string[]]$Arguments) {
    & dotnet @Arguments *> (Join-Path $buildRoot ($Name + '.log'))
    if ($LASTEXITCODE -ne 0) { throw "$Name failed; see build/$Name.log" }
    Write-Output "$Name completed"
}
Push-Location $repo
$previousJavaHome = $env:JAVA_HOME
try {
    $env:JAVA_HOME = $JavaSdk
    & $Python -X utf8 tools/package_internal_delivery.py snapshot --output (Join-Path $buildRoot 'source-before.json')
    if ($LASTEXITCODE -ne 0) { throw 'Source snapshot failed.' }
    Invoke-Recorded 'restore-locked' @('restore', 'HanMate.slnx', '--locked-mode')
    Invoke-Recorded 'windows-publish' @('publish', 'HanMate.App/HanMate.App.csproj', '-c', 'Release', '-f', 'net10.0-windows10.0.19041.0', '--no-restore', '-p:RuntimeIdentifierOverride=win-x64', "-p:OutputPath=$buildRoot/windows-build/", "-p:PublishDir=$buildRoot/windows/", '-v', 'minimal')
    Invoke-Recorded 'android-build' @('build', 'HanMate.App/HanMate.App.csproj', '-c', 'Release', '-f', 'net10.0-android', '--no-restore', "-p:OutputPath=$buildRoot/android/", '-v', 'minimal')
    $apk = Join-Path $buildRoot 'android/com.companyname.hanmate.app-Signed.apk'
    & (Join-Path $AndroidSdk 'build-tools/36.0.0/apksigner.bat') verify --print-certs $apk *> (Join-Path $buildRoot 'android-signature.log')
    if ($LASTEXITCODE -ne 0) { throw 'APK signature inspection failed.' }
    & $Python -X utf8 tools/release_preflight.py --apk $apk --aapt (Join-Path $AndroidSdk 'build-tools/36.0.0/aapt.exe') --output (Join-Path $buildRoot 'release-preflight.json')
    if ($LASTEXITCODE -notin @(0,2)) { throw 'Release preflight failed to produce a report.' }
    # A blocked public release is expected for this explicitly internal delivery.
    & $Python -X utf8 tools/package_internal_delivery.py assemble --windows (Join-Path $buildRoot 'windows') --apk $apk --source-before (Join-Path $buildRoot 'source-before.json') --preflight (Join-Path $buildRoot 'release-preflight.json') --output (Join-Path $destination 'delivery')
    if ($LASTEXITCODE -ne 0) { throw 'Assembly failed.' }
    & $Python -X utf8 tools/package_internal_delivery.py verify --output (Join-Path $destination 'delivery')
    if ($LASTEXITCODE -ne 0) { throw 'Delivery integrity verification failed.' }
    Write-Output "Internal delivery: $destination/delivery"
    Write-Output 'No application tests, installation, data migration, signing-identity changes or publication were performed.'
} finally { $env:JAVA_HOME = $previousJavaHome; Pop-Location }
