param(
    [switch]$Clean,
    [string]$ReferenceAssemblies
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$manifest = Join-Path $src 'app.manifest'
$icon = Join-Path $src 'app.ico'
$out = Join-Path $dist 'MemOptimizer.exe'

function Get-CscPath {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { throw '找不到 dotnet，请先安装 .NET SDK（本脚本用它自带的 Roslyn 编译器）' }

    $sdks = @()
    foreach ($line in (& dotnet --list-sdks)) {
        if ($line -match '^(\S+)\s+\[(.+)\]$') {
            $sdks += [pscustomobject]@{
                Version = [version]$Matches[1]
                Dir     = Join-Path $Matches[2] $Matches[1]
            }
        }
    }

    foreach ($sdk in ($sdks | Sort-Object Version -Descending)) {
        $candidate = Join-Path $sdk.Dir 'Roslyn\bincore\csc.dll'
        if (Test-Path $candidate) { return $candidate }
    }
    throw '找不到 Roslyn csc.dll，请更新 .NET SDK'
}

function Resolve-ReferenceAssemblies {
    param([string]$Explicit)

    $candidates = @()
    if ($Explicit) { $candidates += $Explicit }
    if ($env:NETFX_REFERENCE_ASSEMBLIES) { $candidates += $env:NETFX_REFERENCE_ASSEMBLIES }
    $candidates += 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
    $candidates += 'C:\Program Files\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path (Join-Path $candidate 'mscorlib.dll'))) { return $candidate }
    }

    # 回退：本地没有参考程序集时（比如干净的 CI 机器）从 NuGet 取官方包
    $url = 'https://www.nuget.org/api/v2/package/Microsoft.NETFramework.ReferenceAssemblies.net48/1.0.3'
    $cache = Join-Path ([System.IO.Path]::GetTempPath()) 'net48-refasm-1.0.3'
    $inner = Join-Path $cache 'build\.NETFramework\v4.8'

    if (-not (Test-Path (Join-Path $inner 'mscorlib.dll'))) {
        Write-Host "本地没有 .NET Framework 4.8 参考程序集，改为下载 NuGet 包：$url" -ForegroundColor Yellow
        $zip = Join-Path ([System.IO.Path]::GetTempPath()) 'net48-refasm-1.0.3.zip'
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        if (Test-Path $cache) { Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue }
        New-Item -ItemType Directory -Force -Path $cache | Out-Null
        Expand-Archive -Path $zip -DestinationPath $cache -Force
    }

    if (-not (Test-Path (Join-Path $inner 'mscorlib.dll'))) {
        throw "参考程序集解压后仍找不到 mscorlib.dll：$inner"
    }
    return $inner
}

if ($Clean -and (Test-Path $dist)) {
    Remove-Item (Join-Path $dist '*') -Force -Recurse -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$ref = Resolve-ReferenceAssemblies -Explicit $ReferenceAssemblies
$csc = Get-CscPath
$sources = @(Get-ChildItem (Join-Path $src '*.cs') | ForEach-Object { $_.FullName })

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
$dotnetArgs.Add($csc)
foreach ($item in $cscArgs) { $dotnetArgs.Add($item) }

Write-Host "参考程序集：$ref" -ForegroundColor DarkGray
Write-Host '编译中...' -ForegroundColor Cyan
& dotnet $dotnetArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$file = Get-Item $out
$hash = (Get-FileHash $out -Algorithm SHA256).Hash
Write-Host ('完成：{0}  ({1:N0} 字节)' -f $file.FullName, $file.Length) -ForegroundColor Green
Write-Host ('SHA256：{0}' -f $hash) -ForegroundColor Green
