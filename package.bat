@echo off
setlocal
set OUT=artifacts\publish
if exist artifacts rmdir /s /q artifacts

echo [package] Publishing win-x64...
dotnet publish src\GamepadEnhancer\GamepadEnhancer.csproj -c Release -r win-x64 --self-contained false -o %OUT%
if errorlevel 1 (
    echo [package] ERROR: publish failed.
    exit /b 1
)

echo [package] Creating zip...
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath 'artifacts\GamepadEnhancer-win-x64.zip' -Force"
if errorlevel 1 (
    echo [package] ERROR: zip failed.
    exit /b 1
)

echo [package] Done: artifacts\GamepadEnhancer-win-x64.zip
endlocal
