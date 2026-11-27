@echo off
cd /d "%~dp0"
echo Compilando la solucion (Core + App Avalonia + Tests). La primera vez descarga los paquetes de Avalonia...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0compilar_app.ps1"
echo.
echo Listo. Revisa build-app-log.txt en esta carpeta.
pause
