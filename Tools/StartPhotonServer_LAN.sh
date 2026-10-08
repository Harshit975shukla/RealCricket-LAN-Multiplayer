#!/bin/bash
# ============================================
# RealCricket LAN Multiplayer - Photon Server Launcher (Linux/Mac)
# ============================================

echo "Starting RealCricket LAN Photon Server..."
echo ""

# Check if Photon Server is installed
PHOTON_PATH="$HOME/Photon/deploy/bin_Linux"
if [ ! -f "$PHOTON_PATH/PhotonSocketServer.exe" ]; then
    # Try alternative paths
    if [ -f "/opt/Photon/deploy/bin_Linux/PhotonSocketServer.exe" ]; then
        PHOTON_PATH="/opt/Photon/deploy/bin_Linux"
    elif [ -f "$HOME/Photon/deploy/bin_OSX/PhotonSocketServer.exe" ]; then
        PHOTON_PATH="$HOME/Photon/deploy/bin_OSX"
    else
        echo "ERROR: Photon Server not found"
        echo "Please install Photon Server SDK from https://www.photonengine.com/en-us/PhotonServer"
        echo "Expected paths:"
        echo "  - $HOME/Photon/deploy/bin_Linux/PhotonSocketServer.exe"
        echo "  - /opt/Photon/deploy/bin_Linux/PhotonSocketServer.exe"
        echo "  - $HOME/Photon/deploy/bin_OSX/PhotonSocketServer.exe"
        exit 1
    fi
fi

# Get local IP for LAN
LOCAL_IP=$(ip route get 1.1.1.1 2>/dev/null | awk '{print $7; exit}')
if [ -z "$LOCAL_IP" ]; then
    LOCAL_IP=$(ifconfig | grep -Eo 'inet (addr:)?([0-9]*\.){3}[0-9]*' | grep -Eo '([0-9]*\.){3}[0-9]*' | grep -v '127.0.0.1' | head -1)
fi
echo "Local IP detected: $LOCAL_IP"
echo ""

# Create config if not exists
if [ ! -f "$PHOTON_PATH/PhotonServer_LAN.config" ]; then
    echo "Creating LAN config..."
    cp "$(dirname "$0")/../PhotonServer_LAN.config" "$PHOTON_PATH/PhotonServer_LAN.config"
fi

# Check if mono is installed
if ! command -v mono &> /dev/null; then
    echo "ERROR: mono not found. Please install mono to run Photon Server on Linux/Mac"
    echo "Ubuntu/Debian: sudo apt install mono-complete"
    echo "Mac: brew install mono"
    exit 1
fi

# Start Photon Server
echo "Starting Photon Master Server on port 5055..."
echo "Starting Photon Game Server on port 5056..."
echo ""
echo "Press Ctrl+C to stop the server"
echo ""

cd "$PHOTON_PATH"
mono PhotonSocketServer.exe /run /config PhotonServer_LAN.config