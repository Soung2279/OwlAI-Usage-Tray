$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryDir = Split-Path -Parent $projectDir
$outputDir = Join-Path $repositoryDir 'dist'
$iconPath = Join-Path $projectDir 'icon.ico'

if (-not (Test-Path -LiteralPath $iconPath)) {
    Write-Warning 'icon.ico 不存在；本次开发构建将使用默认程序图标。正式发布脚本会将其视为错误。'
}

dotnet publish (Join-Path $projectDir 'OwlUsageTray.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $outputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出代码：$LASTEXITCODE。请确认旧版程序未在运行。"
}

Copy-Item -LiteralPath (Join-Path $projectDir 'install.ps1') -Destination $outputDir -Force
Copy-Item -LiteralPath (Join-Path $repositoryDir 'README.md') -Destination $outputDir -Force

Write-Host "Built: $(Join-Path $outputDir 'OwlUsageTray.exe')"
