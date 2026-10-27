param([string]$Dotnet, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectDefinition = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\Cofre.App\Cofre.App.csproj') -Raw)
$appVersion = $projectDefinition.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot "publish\$appVersion" }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$targetExe = Join-Path $OutputDirectory 'Cofre.exe'
if (Get-Process Cofre -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe }) {
    throw 'Feche esta versão do Cofre pelo menu da bandeja antes de recompilar.'
}
if (-not $Dotnet) {
    $localSdk = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
}
# Bundle WPF native DLLs too; otherwise a clean output folder can compile successfully and crash at startup.
& $Dotnet publish (Join-Path $projectRoot 'src\Cofre.App') -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutputDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o aplicativo.' }
& $Dotnet publish (Join-Path $projectRoot 'src\Cofre.NativeHost') -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o (Join-Path $OutputDirectory 'native') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar a integração nativa.' }
& (Join-Path $PSScriptRoot 'register-extension.ps1') -AppDirectory $OutputDirectory
Write-Output "Aplicativo publicado em: $targetExe"

# Update the installed entrypoints, including when a previous version is still running.
$shortcutShell = New-Object -ComObject WScript.Shell
foreach ($shortcutFolder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $shortcutPath = Join-Path $shortcutFolder 'Cofre.lnk'
    if (Test-Path -LiteralPath $shortcutPath) {
        $existingShortcut = $shortcutShell.CreateShortcut($shortcutPath)
        if (-not $existingShortcut.TargetPath.StartsWith(($projectRoot + '\publish\'), [StringComparison]::OrdinalIgnoreCase)) {
            $shortcutPath = Join-Path $shortcutFolder 'Cofre - safe-passwords.lnk'
        }
    }
    $shortcut = $shortcutShell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $OutputDirectory
    $shortcut.IconLocation = "$targetExe,0"
    $shortcut.Description = "Cofre $appVersion - Gerenciador de senhas local"
    $shortcut.Save()
}
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$existingStartup = (Get-ItemProperty -LiteralPath $startupKey -ErrorAction SilentlyContinue).Cofre
if ($existingStartup -is [string] -and $existingStartup.Trim('"').StartsWith(($projectRoot + '\publish\'), [StringComparison]::OrdinalIgnoreCase)) {
    Set-ItemProperty -LiteralPath $startupKey -Name 'Cofre' -Value ('"' + $targetExe + '"')
}
Write-Output 'Atalhos atualizados na Área de Trabalho e no menu Iniciar.'
