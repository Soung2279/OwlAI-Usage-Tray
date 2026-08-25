$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $scriptDir 'OwlUsageTray.exe'

if (-not (Test-Path -LiteralPath $exePath)) {
    $buildScript = Join-Path $scriptDir 'build.ps1'
    if (Test-Path -LiteralPath $buildScript) {
        & $buildScript
        $exePath = Join-Path (Split-Path -Parent $scriptDir) 'dist\OwlUsageTray.exe'
    }
}

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "找不到分发程序：$exePath"
}

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'OwlAI 用量监控.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = Split-Path -Parent $exePath
$shortcut.Description = 'OwlAI 实时用量监控'
$shortcut.Save()

Start-Process -FilePath $exePath
Write-Host "Desktop shortcut created: $shortcutPath"
