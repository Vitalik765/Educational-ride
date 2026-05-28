@echo off
echo Сборка проекта Менеджер программных продуктов...
echo.

REM Проверка наличия .NET 8
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ОШИБКА: .NET 8 не найден. Пожалуйста, установите .NET 8 SDK.
    pause
    exit /b 1
)

echo Восстановление NuGet пакетов...
dotnet restore

if %errorlevel% neq 0 (
    echo ОШИБКА: Не удалось восстановить пакеты.
    pause
    exit /b 1
)

echo Сборка проекта...
dotnet build --configuration Release

if %errorlevel% neq 0 (
    echo ОШИБКА: Не удалось собрать проект.
    pause
    exit /b 1
)

echo.
echo Сборка завершена успешно!
echo Исполняемый файл находится в папке: bin\Release\net8.0-windows\
echo.
echo Убедитесь, что файл базы данных 123.accdb находится в папке с исполняемым файлом.
echo.
pause
