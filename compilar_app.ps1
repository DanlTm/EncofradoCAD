# EncofradoCAD v2 - Compila con .NET 10 local o con el SDK del sistema
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot
$log    = Join-Path $PSScriptRoot 'build-app-log.txt'
$dotnet = Join-Path $PSScriptRoot '.dotnet10\dotnet.exe'

if (-not (Test-Path $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) {
        $installedSdks = & $dotnetCommand.Source --list-sdks
        if ($installedSdks -match '(?m)^10\.') {
            $dotnet = $dotnetCommand.Source
        }
    }
}

"============================================================" | Out-File -FilePath $log -Encoding utf8
"  EncofradoCAD v2 - build de la solucion  ($(Get-Date))"      | Add-Content $log
"============================================================" | Add-Content $log

if (-not (Test-Path $dotnet)) {
    "ERROR: no se encontro el SDK de .NET 10. Ejecuta probar_motor.bat o instala .NET 10." | Add-Content $log
    return
}

& $dotnet build (Join-Path $PSScriptRoot 'EncofradoCAD.sln') -c Debug --nologo *>> $log

"" | Add-Content $log
"=== FIN build $(Get-Date) ===" | Add-Content $log
