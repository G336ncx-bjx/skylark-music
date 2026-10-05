param(
    [string]$Apk,
    [string]$PlayerExe
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $Apk) { $Apk = Join-Path $repo 'dist\Skylark-android.apk' }
if (-not $PlayerExe) { $PlayerExe = Join-Path $repo 'dist\release\Skylark.exe' }
if (-not (Test-Path -LiteralPath $PlayerExe)) { $PlayerExe = Join-Path $repo 'dist\Skylark.exe' }
if (-not $env:SKYLARK_UPDATE_TOKEN) { throw 'Set SKYLARK_UPDATE_TOKEN to the update repository write token first' }
if (-not (Test-Path -LiteralPath $Apk) -or -not (Test-Path -LiteralPath $PlayerExe)) { throw 'Build the APK and Windows executable first' }
$Apk = (Resolve-Path -LiteralPath $Apk).Path
$PlayerExe = (Resolve-Path -LiteralPath $PlayerExe).Path

[xml]$manifest = Get-Content -LiteralPath (Join-Path $repo 'android\AndroidManifest.xml') -Raw
$version = $manifest.manifest.GetAttribute('versionName', 'http://schemas.android.com/apk/res/android')
$versionCode = $manifest.manifest.GetAttribute('versionCode', 'http://schemas.android.com/apk/res/android')
$source = Get-Content -LiteralPath (Join-Path $repo 'android\src\com\skylark\music\Update.java') -Raw
$readToken = [regex]::Match($source, 'UPDATE_ENDPOINT\s*=\s*"([^"]+)"').Groups[1].Value
$work = Join-Path $repo 'dist\update-publish'
New-Item -ItemType Directory -Force -Path $work | Out-Null
Copy-Item -LiteralPath $PlayerExe -Destination (Join-Path $work 'Skylark.exe') -Force
$helper = Join-Path $work 'PublishAndroidUpdate.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /codepage:65001 "/reference:$work\Skylark.exe" "/out:$helper" (Join-Path $repo 'scripts\PublishAndroidUpdate.cs')
if ($LASTEXITCODE -ne 0) { throw 'Could not compile the update publication helper' }

function Invoke-PublishHelper([string]$mode) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $helper
    $start.Arguments = "$mode `"$Apk`" $version"
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($start)
    $process.StandardInput.WriteLine($env:SKYLARK_UPDATE_TOKEN)
    $process.StandardInput.WriteLine($readToken)
    $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEnd()
    $errorText = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($output) { Write-Output $output.Trim() }
    if ($process.ExitCode -ne 0) { throw "Update publication failed: $errorText" }
    $process.Dispose()
}

$sdkRoots = @((Join-Path $repo '.toolchain'), $env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk'))
function Find-AndroidTool([string]$name) {
    foreach ($sdkRoot in $sdkRoots) {
        if (-not $sdkRoot -or -not (Test-Path -LiteralPath $sdkRoot)) { continue }
        $found = Get-ChildItem -LiteralPath $sdkRoot -Filter $name -Recurse -File | Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    throw "Android SDK tool not found: $name"
}
$signer = Find-AndroidTool 'apksigner.bat'
$aapt = Find-AndroidTool 'aapt2.exe'
if (-not $env:JAVA_HOME) {
    $javac = Find-AndroidTool 'javac.exe'
    $env:JAVA_HOME = Split-Path -Parent (Split-Path -Parent $javac)
}
function Get-ApkCertificate([string]$path) {
    $certificate = & $signer verify --print-certs $path
    if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed' }
    $digest = @($certificate | Where-Object { $_ -match '^Signer #\d+ certificate SHA-256 digest:' })
    if ($digest.Count -eq 0) { throw 'APK has no signing certificate' }
    return ($digest -join ';')
}

# aapt2 cannot handle non-ASCII paths; stage APKs under the system temp folder.
$toolWork = Join-Path $env:TEMP ('skylark-update-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $toolWork | Out-Null
try {
    $candidate = Join-Path $toolWork 'candidate.apk'
    Copy-Item -LiteralPath $Apk -Destination $candidate
    $badging = & $aapt dump badging $candidate
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect APK metadata' }
    $package = $badging | Select-Object -First 1
    if ($package -notmatch "name='com.skylark.music'" -or $package -notmatch "versionCode='$versionCode'" -or $package -notmatch "versionName='$([regex]::Escape($version))'") {
        throw 'APK package/version does not match the source manifest'
    }
    $currentCert = Get-ApkCertificate $candidate
    Invoke-PublishHelper 'inspect'
    $previous = Join-Path $toolWork 'previous.apk'
    Copy-Item -LiteralPath (Join-Path $work 'previous-update.apk') -Destination $previous
    if ($currentCert -ne (Get-ApkCertificate $previous)) { throw 'Signing certificate differs from the published APK; cannot provide an in-place update' }
    $previousMetadata = & $aapt dump badging $previous
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the previously published APK' }
    $previousPackage = $previousMetadata | Select-Object -First 1
    $previousCode = [regex]::Match($previousPackage, "versionCode='(\d+)'").Groups[1].Value
    if ($previousPackage -notmatch "name='com.skylark.music'" -or -not $previousCode -or [int]$previousCode -gt [int]$versionCode) {
        throw 'Published APK package or versionCode is incompatible with this update'
    }
    Write-Output "Package and signing certificate verified: $version (versionCode $versionCode)"
    Invoke-PublishHelper 'publish'
} finally {
    $resolvedToolWork = [IO.Path]::GetFullPath($toolWork)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if ($resolvedToolWork.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolvedToolWork).StartsWith('skylark-update-check-')) {
        Remove-Item -LiteralPath $resolvedToolWork -Recurse -Force
    }
}
