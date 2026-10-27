param([switch]$Uninstall, [string]$AppDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$hostName = 'com.cofre.passwords'
$registryRoots = @('HKCU:\Software\Google\Chrome\NativeMessagingHosts', 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts')
if ($Uninstall) {
    foreach ($registryRoot in $registryRoots) {
        $registryPath = Join-Path $registryRoot $hostName
        if (Test-Path -LiteralPath $registryPath) { Remove-Item -LiteralPath $registryPath }
    }
    Write-Output 'Registro da integração removido para Chrome e Edge.'
    return
}
if (-not $AppDirectory) {
    $projectDefinition = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\Cofre.App\Cofre.App.csproj') -Raw)
    $appVersion = $projectDefinition.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $AppDirectory = Join-Path $projectRoot "publish\$appVersion"
}
$nativeFolder = Join-Path ([System.IO.Path]::GetFullPath($AppDirectory)) 'native'
$hostExe = Join-Path $nativeFolder 'Cofre.NativeHost.exe'
if (-not (Test-Path -LiteralPath $hostExe)) { throw 'Compile primeiro com tools\publish.ps1.' }
$extensionManifest = Get-Content -LiteralPath (Join-Path $projectRoot 'extension\manifest.json') -Raw | ConvertFrom-Json
$keyBytes = [Convert]::FromBase64String($extensionManifest.key)
$hasher = [System.Security.Cryptography.SHA256]::Create()
$hash = $hasher.ComputeHash($keyBytes)
$hasher.Dispose()
$extensionId = -join ($hash[0..15] | ForEach-Object { [char](97 + ($_ -shr 4)); [char](97 + ($_ -band 15)) })
$manifestPath = Join-Path $nativeFolder ($hostName + '.json')
$manifest = [ordered]@{
    name = $hostName
    description = 'Cofre - integração local de senhas'
    path = $hostExe
    type = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
}
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
foreach ($registryRoot in $registryRoots) {
    $registryPath = Join-Path $registryRoot $hostName
    New-Item -Path $registryPath -Force | Out-Null
    Set-Item -LiteralPath $registryPath -Value $manifestPath
}
Write-Output "Integração registrada para Chrome e Edge. ID: $extensionId"
Write-Output ('Carregue a pasta em modo de desenvolvedor: ' + (Join-Path $projectRoot 'extension'))
