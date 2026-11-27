@echo off
REM ============================================================
REM   EncofradoCAD v2 - Menu de inicio
REM   [1] Iniciar el programa   [2] Salir
REM   Al cerrarse el programa, vuelve a mostrar este menu.
REM ============================================================
setlocal
cd /d "%~dp0"

:menu
cls
echo ============================================================
echo    EncofradoCAD v2
echo ============================================================
echo.
echo   [1] Iniciar el programa
echo   [2] Salir
echo.
set "opcion="
set /p "opcion=Selecciona una opcion (1-2): "

if "%opcion%"=="1" goto iniciar
if "%opcion%"=="2" goto salir
echo.
echo Opcion no valida. Intenta de nuevo.
timeout /t 2 >nul
goto menu

:iniciar
cls
echo Compilando y ejecutando EncofradoCAD v2...
echo (La primera vez puede tardar mientras prepara .NET 10 y restaura Avalonia)
echo.

set "DOTNET=%~dp0.dotnet10\dotnet.exe"
if exist "%DOTNET%" goto ejecutar

where dotnet >nul 2>nul
if errorlevel 1 goto instalar_sdk
dotnet --list-sdks | findstr /R /B "10\." >nul
if errorlevel 1 goto instalar_sdk
set "DOTNET=dotnet"
goto ejecutar

:instalar_sdk
echo No se encontro el SDK de .NET 10. Instalando una copia local...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0probar_motor.ps1" -InstallOnly
if errorlevel 1 goto error_sdk
set "DOTNET=%~dp0.dotnet10\dotnet.exe"
if not exist "%DOTNET%" goto error_sdk

:ejecutar
"%DOTNET%" run --project "%~dp0EncofradoCAD.App\EncofradoCAD.App.csproj" -c Debug
echo.
echo ------------------------------------------------------------
echo La aplicacion se cerro.
echo.
pause
goto menu

:error_sdk
echo No se pudo preparar .NET 10. Revisa test-log.txt y vuelve a intentarlo.
echo.
pause
goto menu

:salir
endlocal
exit /b 0
