# publish.ps1 — 一键打包两个单文件 EXE
# 用法:  .\publish.ps1
# 产物:  dist/GDCnvQstEditor.exe       GUI（带参数时也支持 CLI；需 .NET 8 Desktop Runtime x64）
#        dist/GDCnvQstEditor-cli.exe   纯 CLI（需 .NET 8 Runtime x64）
# 语言包已内嵌进 EXE（单文件即可切换语言）；如需自定义/新增语言，在 EXE 旁自建 lang\
# 目录放入 json（同名 code 优先于内嵌包），可用 GUI 菜单"帮助/语言→打开语言包目录"直达。
# 两者均为配置 B / lite：框架依赖单文件。
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$guiProj = Join-Path $root "GDCnvQstEditor\GDCnvQstEditor.csproj"
$cliProj = Join-Path $root "GDCnvQstEditor-cli\GDCnvQstEditor-cli.csproj"
$dist = Join-Path $root "dist"
$tmp = Join-Path $root ".publish-tmp"

Write-Host "==> 打包 GDCnvQstEditor（GUI+CLI）与 GDCnvQstEditor-cli（纯 CLI）"
Write-Host "    配置 B：框架依赖单文件，需目标机安装 .NET 8 运行时"

# dist 可同时存放测试样本，发布时只覆盖两个 EXE，不清理其他文件
New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }

try {
  dotnet publish $guiProj -c Release -r win-x64 `
    -p:PublishSingleFile=true `
    -p:SelfContained=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o (Join-Path $tmp "gui")
  if ($LASTEXITCODE -ne 0) { throw "GDCnvQstEditor 发布失败 (exit=$LASTEXITCODE)" }

  dotnet publish $cliProj -c Release -r win-x64 `
    -p:PublishSingleFile=true `
    -p:SelfContained=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o (Join-Path $tmp "cli")
  if ($LASTEXITCODE -ne 0) { throw "GDCnvQstEditor-cli 发布失败 (exit=$LASTEXITCODE)" }

  Copy-Item (Join-Path $tmp "gui\GDCnvQstEditor.exe") (Join-Path $dist "GDCnvQstEditor.exe") -Force
  Copy-Item (Join-Path $tmp "cli\GDCnvQstEditor-cli.exe") (Join-Path $dist "GDCnvQstEditor-cli.exe") -Force
}
finally {
  if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
}

$guiExe = Join-Path $dist "GDCnvQstEditor.exe"
$cliExe = Join-Path $dist "GDCnvQstEditor-cli.exe"
foreach ($p in @($guiExe, $cliExe)) {
  if (-not (Test-Path $p)) { throw "未找到产物 $p" }
}

Write-Host ""
Write-Host "生成完成。"
foreach ($p in @($guiExe, $cliExe)) {
  $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($p)
  $hash = Get-FileHash $p -Algorithm SHA256
  $size = (Get-Item $p).Length
  Write-Host ("  {0}" -f (Split-Path $p -Leaf))
  Write-Host ("    版本   : {0}" -f $info.FileVersion)
  Write-Host ("    大小   : {0:N0} B ({1:N2} MB)" -f $size, ($size / 1MB))
  Write-Host ("    SHA256 : {0}" -f $hash.Hash)
}
Write-Host ""
Write-Host "使用:"
Write-Host "  dist\GDCnvQstEditor.exe                    # 无参数 -> 图形界面"
Write-Host "  dist\GDCnvQstEditor.exe verify <目录>      # 带参数 -> CLI（与 GDCnvQstEditor-cli 相同）"
Write-Host "  dist\GDCnvQstEditor-cli.exe help                 # CLI 帮助"
Write-Host "  dist\GDCnvQstEditor-cli.exe verify <目录>        # 示例：批量校验"
Write-Host "（GUI 需 .NET 8 Desktop Runtime x64；纯 CLI 需 .NET 8 Runtime x64：https://dotnet.microsoft.com/download/dotnet/8.0）"
