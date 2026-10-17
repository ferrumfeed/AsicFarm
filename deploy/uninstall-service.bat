@echo off
REM Удаление службы AsicFarm.Watchdog
REM Запускать от имени Администратора

set SERVICE_NAME=AsicFarm.Watchdog

echo Останавливаю службу...
sc stop %SERVICE_NAME% >nul 2>&1
timeout /t 3 /nobreak >nul

echo Удаляю службу...
sc delete %SERVICE_NAME%

echo.
echo Служба удалена.
pause