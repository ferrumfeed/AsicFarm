@echo off
REM Установка AsicFarm.Watchdog как Windows Service
REM Запускать от имени Администратора

set SERVICE_NAME=AsicFarm.Watchdog
set EXE_PATH=%~dp0AsicFarm.Watchdog.exe

if not exist "%EXE_PATH%" (
    echo [ОШИБКА] Не найден %EXE_PATH%
    echo Скопируй этот скрипт рядом с AsicFarm.Watchdog.exe
    pause
    exit /b 1
)

echo Удаляю старую службу, если есть...
sc stop %SERVICE_NAME% >nul 2>&1
sc delete %SERVICE_NAME% >nul 2>&1
timeout /t 2 /nobreak >nul

echo Устанавливаю службу...
sc create %SERVICE_NAME% binPath= "\"%EXE_PATH%\"" start= auto DisplayName= "AsicFarm Watchdog"
sc description %SERVICE_NAME% "Мониторинг асиков и отправка телеметрии на сервер AsicFarm"

echo Настраиваю автоперезапуск при падении...
sc failure %SERVICE_NAME% reset= 86400 actions= restart/5000/restart/10000/restart/30000

echo Запускаю службу...
sc start %SERVICE_NAME%

echo.
echo ============================================
echo   Готово.
echo   Проверить: sc query %SERVICE_NAME%
echo   Логи: logs\watchdog-YYYYMMDD.log (рядом с exe)
echo ============================================
pause