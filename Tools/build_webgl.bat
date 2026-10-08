@echo off
REM build_webgl.bat - Build WebGL version and package as single .exe
REM Run this from Unity installation directory or with Unity in PATH

set PROJECT_PATH=%~dp0
set UNITY_PATH=%PROGRAMFILES%\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe

if not exist "%UNITY_PATH%" (
    echo Unity not found at %UNITY_PATH%
    echo Please install Unity 2022.3 LTS or update UNITY_PATH in this script
    pause
    exit /b 1
)

echo ============================================
echo Building RealCricket WebGL for Browser
echo ============================================
echo Project: %PROJECT_PATH%
echo Unity: %UNITY_PATH%
echo.

REM Build WebGL using Unity batch mode
"%UNITY_PATH%" -batchmode -nographics -projectPath "%PROJECT_PATH%" -executeMethod BuildScript.BuildWebGL -quit -logFile -

if errorlevel 1 (
    echo Build failed! Check Editor.log
    pause
    exit /b 1
)

echo.
echo WebGL build successful!
echo Output: %PROJECT_PATH%Builds\WebGL
echo.

REM Check if Python/PyInstaller available for packaging
where python >nul 2>&1
if %errorlevel% neq 0 (
    echo Python not found - skipping .exe packaging
    echo Install Python and run: pip install pyinstaller cryptography websockets
    echo Then run: pyinstaller --onefile --noconsole --add-data "Builds\WebGL;WebGL" launcher.py
    pause
    exit /b 0
)

echo Packaging as single .exe with PyInstaller...
pip install pyinstaller cryptography websockets >nul 2>&1

pyinstaller --onefile --noconsole ^
    --add-data "Builds\WebGL;WebGL" ^
    --add-data "launcher.py;." ^
    --name "RealCricket-LAN" ^
    launcher.py

if errorlevel 1 (
    echo PyInstaller failed
    pause
    exit /b 1
)

echo.
echo ============================================
echo SUCCESS! Single .exe created:
echo %PROJECT_PATH%dist\RealCricket-LAN.exe
echo ============================================
echo.
echo To run: Double-click RealCricket-LAN.exe
echo It will:
echo   1. Extract WebGL build to temp folder
echo   2. Start HTTPS server on https://localhost:8443
echo   3. Open browser automatically
echo   4. Connect to Photon server (configure IP in PhotonServerSettings)
echo.
pause