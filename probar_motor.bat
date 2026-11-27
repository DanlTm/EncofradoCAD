@echo off
cd /d "%~dp0"
echo Ejecutando... esto instala .NET 10 la primera vez (puede tardar). No cierres esta ventana.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0probar_motor.ps1"
echo.
echo Listo. Revisa test-log.txt en esta carpeta.
pause
