# 构建 PCL CE Mod
#
#   .\build.ps1                       # 只构建，输出到 dist\（并顺便检查补丁依赖是否齐全）
#   .\build.ps1 -DeployTo 'D:\PCL-CE-Mod'   # 构建后部署到指定目录
#
# 需要 .NET 10 SDK（编译期）；运行需要 .NET 10 Desktop Runtime（PCL CE 本身就需要）。

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$DeployTo = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1)?.Source
if (-not $dotnet) {
    $candidate = Join-Path $env:USERPROFILE '.dotnet10\dotnet.exe'
    if (Test-Path $candidate) { $dotnet = $candidate }
}
if (-not $dotnet) { throw '找不到 dotnet，请先安装 .NET 10 SDK。' }

Write-Host '==> 构建补丁 DLL' -ForegroundColor Cyan
& $dotnet publish (Join-Path $root 'src\patch\PCLCeMod.csproj') -c $Configuration -o (Join-Path $dist 'patch')
if ($LASTEXITCODE -ne 0) { throw '补丁构建失败' }

foreach ($required in @('PCLCeMod.dll', '0Harmony.dll')) {
    $path = Join-Path $dist "patch\$required"
    if (-not (Test-Path $path)) { throw "构建产物缺少 $required" }
}

Write-Host '==> 构建启动器' -ForegroundColor Cyan
& $dotnet publish (Join-Path $root 'src\launcher\PCLCeModLauncher.csproj') -c $Configuration -o $dist
if ($LASTEXITCODE -ne 0) { throw '启动器构建失败' }

# 清理 patch 目录里用不到的产物，只留运行时需要的
Get-ChildItem (Join-Path $dist 'patch') -File |
    Where-Object { $_.Name -notin @('PCLCeMod.dll', '0Harmony.dll') } |
    Remove-Item -Force

Write-Host "==> 构建完成：$dist" -ForegroundColor Green
Get-ChildItem $dist -Recurse -File | Select-Object Length, FullName | Format-Table -AutoSize

if ($DeployTo) {
    Write-Host "==> 部署到 $DeployTo" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $DeployTo -Force | Out-Null
    Copy-Item (Join-Path $dist 'PCL-CE-Mod.exe') $DeployTo -Force
    New-Item -ItemType Directory -Path (Join-Path $DeployTo 'patch') -Force | Out-Null
    Copy-Item (Join-Path $dist 'patch\*') (Join-Path $DeployTo 'patch') -Force

    $configPath = Join-Path $DeployTo 'pcl-ce-mod.json'
    if (-not (Test-Path $configPath)) {
        Copy-Item (Join-Path $root 'config\pcl-ce-mod.example.json') $configPath
        Write-Host '已生成默认 pcl-ce-mod.json（第一次部署）' -ForegroundColor Yellow
    }
    else {
        Write-Host '保留已有的 pcl-ce-mod.json' -ForegroundColor Yellow
    }

    Write-Host '==> 部署完成' -ForegroundColor Green
}
