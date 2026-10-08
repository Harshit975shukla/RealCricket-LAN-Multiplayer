@echo off
REM ============================================
REM RealCricket LAN Multiplayer - Photon Server Launcher
REM ============================================

echo Starting RealCricket LAN Photon Server...
echo.

REM Check if Photon Server is installed
set PHOTON_PATH=C:\Photon\deploy\bin_Win64
if not exist "%PHOTON_PATH%\PhotonSocketServer.exe" (
    echo ERROR: Photon Server not found at %PHOTON_PATH%
    echo Please install Photon Server SDK from https://www.photonengine.com/en-us/PhotonServer
    echo Expected path: C:\Photon\deploy\bin_Win64\PhotonSocketServer.exe
    pause
    exit /b 1
)

REM Get local IP for LAN
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /i "IPv4"') do set LOCAL_IP=%%a
set LOCAL_IP=%LOCAL_IP: =%
echo Local IP detected: %LOCAL_IP%
echo.

REM Create config if not exists
if not exist "%PHOTON_PATH%\PhotonServer_LAN.config" (
    echo Creating LAN config...
    copy "%~dp0PhotonServer_LAN.config" "%PHOTON_PATH%\PhotonServer_LAN.config"
)

REM Start Photon Server
echo Starting Photon Master Server on port 5055...
echo Starting Photon Game Server on port 5056...
echo.
echo Press Ctrl+C to stop the server
echo.

cd /d "%PHOTON_PATH%"
PhotonSocketServer.exe /run /config PhotonServer_LAN.config

pause