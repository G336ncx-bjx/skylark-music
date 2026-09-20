<#
    云雀 Skylark - Android 构建脚本
    不使用 Gradle：aapt2 编译资源 -> javac 编译 -> d8 生成 dex -> zipalign 对齐 -> apksigner 签名。

    工具查找顺序：仓库内 .toolchain -> 环境变量 ANDROID_HOME / ANDROID_SDK_ROOT -> %LOCALAPPDATA%\Android\Sdk。
    说明：aapt2 / zipalign 是原生程序，遇到中文路径会报「找不到目录」，
          所以编译在 %TEMP%\skylark-android-build 里进行，最后把 APK 复制回 dist。

    产物：dist\Skylark-android.apk（签名固定，可直接安装并覆盖升级）
#>
param(
    [switch]$SkipTest,
    [switch]$RequireKeyStore,
    [int]$VersionCode = 30323,
    [string]$VersionName = '3.3.23',
    [string]$KeyStore,
    [string]$KeyAlias = 'skylark',
    [string]$KeyPass
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }

$root     = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo     = Split-Path -Parent $root
$dist     = Join-Path $repo 'dist'
$apk      = Join-Path $dist 'Skylark-android.apk'
$minSdk   = 21
$targetSdk = 34

# 签名密钥与口令都不进仓库：
#   1) -KeyPass / $env:SKYLARK_KS_PASS / android\keystore.pass
#   2) 密钥文件默认 android\skylark.jks，不存在时自动生成一个（并提示备份）
$keystore = if ($KeyStore) { $KeyStore } else { Join-Path $root 'skylark.jks' }
$passFile = Join-Path $root 'keystore.pass'
$keyPass = $KeyPass
if (-not $keyPass -and $env:SKYLARK_KS_PASS) { $keyPass = $env:SKYLARK_KS_PASS }
if (-not $keyPass -and (Test-Path $passFile)) { $keyPass = (Get-Content $passFile -Raw).Trim() }
if ($RequireKeyStore -and -not (Test-Path $keystore)) {
    throw "缺少签名密钥 $keystore：请配置 GitHub Secret ANDROID_KEYSTORE_BASE64 / ANDROID_KEYSTORE_PASS"
}

# 编译目录必须是没有中文、没有空格的路径
$work = Join-Path $env:TEMP 'skylark-android-build'
if ($work -match '[^\x20-\x7E]') { $work = Join-Path $env:SystemDrive 'skylark-android-build' }

Write-Output '==> 查找构建工具'

$bases = @()
$toolchain = Join-Path $repo '.toolchain'
if (Test-Path $toolchain) { $bases += $toolchain }
foreach ($name in @('ANDROID_HOME', 'ANDROID_SDK_ROOT')) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ($value -and (Test-Path $value)) { $bases += $value }
}
$localSdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
if (Test-Path $localSdk) { $bases += $localSdk }
if ($bases.Count -eq 0) { throw '找不到 Android SDK，也找不到仓库内的 .toolchain 目录' }

function Resolve-Tool([string]$fileName) {
    $found = @()
    foreach ($base in $bases) {
        $found += Get-ChildItem -Path $base -Filter $fileName -Recurse -File -ErrorAction SilentlyContinue
    }
    if ($found.Count -eq 0) { return $null }
    return ($found | Sort-Object FullName -Descending | Select-Object -First 1).FullName
}

$jdkHome = $null
if ($env:JAVA_HOME -and (Test-Path (Join-Path $env:JAVA_HOME 'bin\javac.exe'))) {
    $jdkHome = $env:JAVA_HOME
}
if (-not $jdkHome) {
    foreach ($base in $bases) {
        $javacFound = Get-ChildItem -Path $base -Filter javac.exe -Recurse -File -ErrorAction SilentlyContinue |
                      Sort-Object FullName -Descending | Select-Object -First 1
        if ($javacFound) { $jdkHome = $javacFound.Directory.Parent.FullName; break }
    }
}

$aapt2      = Resolve-Tool 'aapt2.exe'
$zipalign   = Resolve-Tool 'zipalign.exe'
$apksigner  = Resolve-Tool 'apksigner.bat'
$d8         = Resolve-Tool 'd8.bat'
$androidJar = Resolve-Tool 'android.jar'

foreach ($pair in @(
        @('aapt2', $aapt2), @('zipalign', $zipalign), @('apksigner', $apksigner),
        @('d8', $d8), @('android.jar', $androidJar))) {
    if (-not $pair[1]) { throw "找不到 $($pair[0])，请设置 ANDROID_HOME 或准备 .toolchain 目录" }
    Write-Output "    $($pair[0]): $($pair[1])"
}
if (-not $jdkHome) { throw '找不到 JDK：请设置 JAVA_HOME' }
$javacExe = Join-Path $jdkHome 'bin\javac.exe'
$javaExe = Join-Path $jdkHome 'bin\java.exe'
$jarExe = Join-Path $jdkHome 'bin\jar.exe'
$env:JAVA_HOME = $jdkHome
Write-Output "    jdk: $jdkHome"

# ---------------------------------------------------------------- 签名密钥

if (-not (Test-Path $keystore)) {
    $chars = 'abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    if (-not $keyPass) {
        $keyPass = ''
        for ($i = 0; $i -lt 24; $i++) { $keyPass += $chars[(Get-Random -Maximum $chars.Length)] }
    }
    Write-Output '==> 没有找到签名密钥，生成一个新的（只留在本机，不会进仓库）'
    & (Join-Path $jdkHome 'bin\keytool.exe') -genkeypair -v -keystore $keystore `
        -storetype PKCS12 -alias $KeyAlias -keyalg RSA -keysize 2048 -validity 10000 `
        -storepass $keyPass -keypass $keyPass `
        -dname 'CN=Skylark, OU=Personal, O=Skylark, L=Beijing, ST=Beijing, C=CN'
    if ($LASTEXITCODE -ne 0) { throw '生成签名密钥失败' }
    Set-Content -Path $passFile -Value $keyPass -NoNewline -Encoding ASCII
    Write-Output "    口令写在 $passFile"
    Write-Output '    请把这两个文件备份好（丢了口令/密钥就没法覆盖升级已安装的版本）。'
}
if (-not $keyPass) {
    throw "找不到签名口令：可用 -KeyPass 参数、环境变量 SKYLARK_KS_PASS，或写进 $passFile"
}

# ---------------------------------------------------------------- 准备编译目录

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Force -Path $dist | Out-Null }
if (-not $SkipTest -and -not (Test-Path (Join-Path $root 'tools\SelfTest.java'))) {
    throw '缺少 tools\SelfTest.java'
}

Write-Output "==> 准备编译目录 $work"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

if (-not (Test-Path (Join-Path $root 'res\mipmap-xxxhdpi\ic_launcher.png'))) {
    Write-Output '    生成图标'
    & (Join-Path $repo 'scripts\make-android-icon.ps1')
}

Copy-Item (Join-Path $root 'src') (Join-Path $work 'src') -Recurse
Copy-Item (Join-Path $root 'res') (Join-Path $work 'res') -Recurse
Copy-Item (Join-Path $root 'tools') (Join-Path $work 'tools') -Recurse
Copy-Item (Join-Path $root 'AndroidManifest.xml') $work
Copy-Item $keystore (Join-Path $work 'skylark.jks')
# aapt2 也是原生程序，读不了中文路径下的 android.jar，复制一份到编译目录
Copy-Item $androidJar (Join-Path $work 'android.jar')
$androidJar = Join-Path $work 'android.jar'

$src     = Join-Path $work 'src'
$res     = Join-Path $work 'res'
$tools   = Join-Path $work 'tools'
$build   = Join-Path $work 'build'
$classes = Join-Path $build 'classes'
$gen     = Join-Path $build 'gen'
$dexOut  = Join-Path $build 'dex'
foreach ($dir in @($build, $gen, $classes, $dexOut)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

# ---------------------------------------------------------------- 自检（不需要设备）

if (-not $SkipTest) {
    Write-Output '==> 逻辑自检（歌词解析 / 文件名解析 / 时长解析）'
    $testOut = Join-Path $build 'test'
    New-Item -ItemType Directory -Force -Path $testOut | Out-Null
    & $javacExe -encoding UTF-8 -nowarn -d $testOut `
        (Join-Path $src 'com\skylark\music\Util.java') `
        (Join-Path $src 'com\skylark\music\Lrc.java') `
        (Join-Path $tools 'SelfTest.java')
    if ($LASTEXITCODE -ne 0) { throw '自检代码编译失败' }
    & $javaExe -cp $testOut selftest.SelfTest
    if ($LASTEXITCODE -ne 0) { throw '逻辑自检未通过' }
}

# ---------------------------------------------------------------- 编译

Write-Output '==> 编译资源'
$resZip = Join-Path $build 'res.zip'
& $aapt2 compile --dir $res -o $resZip
if ($LASTEXITCODE -ne 0) { throw 'aapt2 compile 失败' }

$baseApk = Join-Path $build 'base.apk'
& $aapt2 link -o $baseApk -I $androidJar --manifest (Join-Path $work 'AndroidManifest.xml') `
    $resZip --java $gen `
    --min-sdk-version $minSdk --target-sdk-version $targetSdk `
    --version-code $VersionCode --version-name $VersionName
if ($LASTEXITCODE -ne 0) { throw 'aapt2 link 失败' }

Write-Output '==> 编译 Java 源码'
$sources = @()
$sources += (Get-ChildItem $src -Recurse -Filter *.java | ForEach-Object { $_.FullName })
$sources += (Get-ChildItem $gen -Recurse -Filter *.java | ForEach-Object { $_.FullName })
& $javacExe -encoding UTF-8 -nowarn -Xlint:-options -source 8 -target 8 `
    -bootclasspath $androidJar -classpath $androidJar -d $classes $sources
if ($LASTEXITCODE -ne 0) { throw 'javac 编译失败' }

Write-Output '==> 生成 dex'
# 类文件多起来会超过 cmd 的命令行长度上限，所以先打成 jar 再交给 d8
$classesJar = Join-Path $build 'classes.jar'
& $jarExe cf $classesJar -C $classes .
if ($LASTEXITCODE -ne 0) { throw '打包 classes.jar 失败' }
& $d8 --release --min-api $minSdk --lib $androidJar --output $dexOut $classesJar
if ($LASTEXITCODE -ne 0) { throw 'd8 失败' }

Write-Output '==> 打包并签名'
$unsigned = Join-Path $build 'unsigned.apk'
Copy-Item $baseApk $unsigned -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($unsigned, 'Update')
try {
    $entry = $zip.GetEntry('classes.dex')
    if ($entry) { $entry.Delete() }
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zip, (Join-Path $dexOut 'classes.dex'), 'classes.dex',
        [System.IO.Compression.CompressionLevel]::NoCompression) | Out-Null
} finally {
    $zip.Dispose()
}

$aligned = Join-Path $build 'aligned.apk'
& $zipalign -f -p 4 $unsigned $aligned
if ($LASTEXITCODE -ne 0) { throw 'zipalign 失败' }

$signedTmp = Join-Path $build 'signed.apk'
& $apksigner sign --ks (Join-Path $work 'skylark.jks') --ks-key-alias $keyAlias `
    --ks-pass "pass:$keyPass" --key-pass "pass:$keyPass" `
    --out $signedTmp $aligned
if ($LASTEXITCODE -ne 0) { throw 'apksigner 签名失败' }

Copy-Item $signedTmp $apk -Force

$certInfo = & $apksigner verify --print-certs $apk
if ($LASTEXITCODE -ne 0) { throw '签名校验失败' }
foreach ($line in $certInfo) {
    if ($line -match 'SHA-256 digest') { Write-Output "    $line" }
}

$size = [math]::Round((Get-Item $apk).Length / 1KB, 1)
$hash = (Get-FileHash $apk -Algorithm SHA256).Hash.ToLower()
Write-Output ''
Write-Output "==> 完成：$apk（$size KB）"
Write-Output "    versionCode=$VersionCode versionName=$VersionName minSdk=$minSdk targetSdk=$targetSdk"
Write-Output "    SHA256 = $hash"
