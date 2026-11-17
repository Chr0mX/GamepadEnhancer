@echo off
setlocal

echo [build] Pulling latest changes...
git pull
if errorlevel 1 (
    echo [build] ERROR: git pull failed.
    exit /b 1
)

echo [build] Restoring NuGet packages...
dotnet restore src\GamepadEnhancer\GamepadEnhancer.csproj
if errorlevel 1 (
    echo [build] ERROR: dotnet restore failed.
    exit /b 1
)

echo [build] Building Release...
dotnet build src\GamepadEnhancer\GamepadEnhancer.csproj -c Release
if errorlevel 1 (
    echo [build] ERROR: build failed.
    exit /b 1
)

echo [build] Done. Binary is in src\GamepadEnhancer\bin\Release\net8.0-windows10.0.19041.0\
endlocal
