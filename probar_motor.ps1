# EncofradoCAD v2 - Instala .NET 10 (local, sin admin) y prueba el motor (Core + Tests)
param(
    [switch]$InstallOnly
)

$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot
$log       = Join-Path $PSScriptRoot 'test-log.txt'
$dotnetDir = Join-Path $PSScriptRoot '.dotnet10'
$dotnet    = Join-Path $dotnetDir 'dotnet.exe'
$installer = Join-Path $PSScriptRoot 'dotnet-install.ps1'

function Log($msg) { $msg | Tee-Object -FilePath $log -Append }

"============================================================" | Out-File -FilePath $log -Encoding utf8
Log " EncofradoCAD v2 - .NET 10 + tests del motor (Core)"
Log " Fecha: $(Get-Date)"
Log "============================================================"

if (-not (Test-Path $dotnet)) {
    if (-not (Test-Path $installer)) {
        Log "[1/3] Descargando el instalador oficial de .NET ..."
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
        } catch {
            Log "ERROR descargando dotnet-install.ps1: $_"
        }
    }
    if (Test-Path $installer) {
        Log "[1/3] Instalando .NET 10 SDK en .dotnet10 (local). Descarga ~200 MB, puede tardar unos minutos ..."
        & $installer -Channel 10.0 -InstallDir $dotnetDir -NoPath *>> $log
    }
} else {
    Log "[1/3] .NET 10 local ya presente, se omite la instalacion."
}

if (-not (Test-Path $dotnet)) {
    Log "ERROR: no se pudo instalar .NET 10. Revisa la conexion a internet y vuelve a ejecutar."
    Log "=== FIN (sin build) $(Get-Date) ==="
    exit 1
}

Log ""
Log "[2/3] Version de .NET instalada:"
& $dotnet --version *>> $log

if ($InstallOnly) {
    Log ".NET 10 listo; se omite la ejecucion de pruebas."
    exit 0
}

Log ""
Log "[3/3] Compilando Core y ejecutando los tests del motor ..."
Log "------------------------------------------------------------"
& $dotnet test (Join-Path $PSScriptRoot 'EncofradoCAD.sln') -c Debug --nologo *>> $log
$testExitCode = $LASTEXITCODE

Log ""
Log "============================================================"
Log " FIN - $(Get-Date)"
Log "============================================================"
exit $testExitCode
