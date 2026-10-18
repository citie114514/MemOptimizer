param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$ref = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'

if (-not (Test-Path $ref)) { throw "缺少 .NET Framework 4.8 引用程序集：$ref" }
if ($Clean -and (Test-Path $dist)) { Remove-Item (Join-Path $dist '*') -Force -Recurse -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$csc = Get-ChildItem 'C:\Program Files\dotnet\sdk\*\Roslyn\bincore\csc.dll' -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $csc) { throw '找不到 Roslyn csc.dll，请安装 .NET SDK' }

$sources = Get-ChildItem (Join-Path $src '*.cs') | ForEach-Object { $_.FullName }
$out = Join-Path $dist 'MemOptimizer.exe'
$manifest = Join-Path $src 'app.manifest'
$icon = Join-Path $src 'app.ico'

$cscArgs = New-Object System.Collections.Generic.List[string]
$cscArgs.Add('/nologo')
$cscArgs.Add('/target:winexe')
$cscArgs.Add('/platform:anycpu')
$cscArgs.Add('/optimize+')
$cscArgs.Add('/deterministic+')
$cscArgs.Add('/langversion:latest')
$cscArgs.Add(('/win32manifest:' + $manifest))
$cscArgs.Add(('/out:' + $out))
if (Test-Path $icon) { $cscArgs.Add(('/win32icon:' + $icon)) }

foreach ($assembly in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')) {
    $path = Join-Path $ref $assembly
    if (-not (Test-Path $path)) { throw "缺少引用程序集：$path" }
    $cscArgs.Add(('/r:' + $path))
}

foreach ($source in $sources) { $cscArgs.Add($source) }

$dotnetArgs = New-Object System.Collections.Generic.List[string]
$dotnetArgs.Add($csc.FullName)
foreach ($item in $cscArgs) { $dotnetArgs.Add($item) }

Write-Host '编译中...' -ForegroundColor Cyan
& dotnet $dotnetArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$file = Get-Item $out
Write-Host ('完成：{0}  ({1:N0} 字节)' -f $file.FullName, $file.Length) -ForegroundColor Green
