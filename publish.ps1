# publish.ps1 — 一键打包 CnvQstStudio 为单文件 EXE（配置 B：依靠运行库的瘦单文件，约 2 MB）
# 用法:  .\publish.ps1
# 产物: dist/CnvQstStudio.exe   （需目标机装有 .NET 8 Desktop Runtime x64）
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "CnvQstStudio\CnvQstStudio.csproj"
$dist = Join-Path $root "dist"

Write-Host "==> 打包 CnvQstStudio（配置 B：lite，依赖 .NET 8 Desktop Runtime）"

# 清理旧产物
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

# 单文件发布（框架依赖，故 TargetFramework 是 net8.0-windows）
dotnet publish $proj -c Release -r win-x64 `
  -p:PublishSingleFile=true `
  -p:SelfContained=false `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $dist

if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit=$LASTEXITCODE)" }

$exe = Join-Path $dist "CnvQstStudio.exe"
if (-not (Test-Path $exe)) { throw "未找到产物 $exe" }

$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
$hash = Get-FileHash $exe -Algorithm SHA256
$size = (Get-Item $exe).Length

Write-Host ""
Write-Host "生成完成。"
Write-Host ("  文件   : {0}" -f $exe)
Write-Host ("  版本   : {0}" -f $info.FileVersion)
Write-Host ("  大小   : {0:N0} B ({1:N2} MB)" -f $size, ($size / 1MB))
Write-Host ("  SHA256 : {0}" -f $hash.Hash)
Write-Host ""
Write-Host "使用:"
Write-Host "  dist\CnvQstStudio.exe                  # 无参数 -> 图形界面"
Write-Host "  dist\CnvQstStudio.exe gui              # 强制图形界面"
Write-Host "  dist\CnvQstStudio.exe verify <目录>    # 带参数 -> CLI"
Write-Host "（配置 B 需要目标机安装 .NET 8 Desktop Runtime x64：https://dotnet.microsoft.com/download/dotnet/8.0）"
