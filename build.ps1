<#
    云雀 Skylark - 构建脚本
    使用 Windows 自带的 .NET Framework C# 编译器 (csc.exe) 生成单文件 exe，
    不依赖任何第三方库或运行时安装。
#>
param(
    [switch]$Debug,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$root  = Split-Path -Parent $MyInvocation.MyCommand.Path
$src   = Join-Path $root 'src'
$dist  = if ($OutDir) { $OutDir } else { Join-Path $root 'dist' }
$assets = Join-Path $root 'assets'
$appName = 'Skylark'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'csc.exe (.NET Framework compiler) not found'
}

# WPF assemblies are not in the framework folder: look them up in the
# reference assemblies first, then in the GAC.
function Resolve-WpfAssembly([string]$name) {
    $refRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework'
    $candidates = @()
    foreach ($version in @('v4.8', 'v4.7.2', 'v4.7', 'v4.6.2', 'v4.6.1', 'v4.6', 'v4.5', 'v4.0')) {
        $candidates += (Join-Path $refRoot "$version\$name.dll")
    }
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    $gac = Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\$name"
    if (Test-Path $gac) {
        $found = Get-ChildItem $gac -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue |
                 Sort-Object FullName | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    throw "assembly not found: $name"
}

$wpfRefs = @{}
foreach ($name in @('WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.Xaml')) {
    $wpfRefs[$name] = Resolve-WpfAssembly $name
}

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

# 图标（不存在时生成）
$icon = Join-Path $assets 'app.ico'
if (-not (Test-Path $icon)) {
    & (Join-Path $root 'scripts\make-icon.ps1')
}

$sources = Get-ChildItem -Path $src -Recurse -Filter *.cs | Sort-Object FullName |
           ForEach-Object { $_.FullName }

$args = New-Object System.Collections.Generic.List[string]
$args.Add('/nologo')
$args.Add('/target:winexe')
$args.Add('/platform:anycpu')
$args.Add('/langversion:5')
$args.Add('/codepage:65001')
$args.Add('/optimize+')
$args.Add('/warn:4')
$args.Add("/out:$dist\$appName.exe")
$args.Add("/win32icon:$icon")
$args.Add('/reference:System.dll')
$args.Add('/reference:System.Core.dll')
$args.Add('/reference:System.Web.Extensions.dll')
$args.Add('/reference:System.Xml.dll')
$args.Add('/reference:System.Xml.Linq.dll')
$args.Add('/reference:System.Drawing.dll')
$args.Add('/reference:System.Windows.Forms.dll')
$args.Add('/reference:System.Runtime.Serialization.dll')
foreach ($name in @('WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.Xaml')) {
    $args.Add("/reference:$($wpfRefs[$name])")
}

# 内嵌 XAML 资源
$args.Add("/resource:$src\Resources\theme.xaml,Skylark.Theme.xaml")
$args.Add("/resource:$src\Resources\templates.xaml,Skylark.Templates.xaml")

if ($Debug) {
    $args.Add('/debug:pdbonly')
    $args.Add('/define:DEBUG')
}

foreach ($s in $sources) { $args.Add($s) }

Write-Host "Compiling $appName.exe ..." -ForegroundColor Cyan
& $csc $args.ToArray()
if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

$exe = Join-Path $dist "$appName.exe"
$size = [math]::Round((Get-Item $exe).Length / 1KB, 1)
Write-Host "Done: $exe ($size KB)" -ForegroundColor Green

