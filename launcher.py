# launcher.py - Single-file launcher that serves WebGL build and handles Photon WebSocket
# Compile to .exe with: pyinstaller --onefile --noconsole --add-data "Builds/WebGL;WebGL" launcher.py

import os
import sys
import ssl
import threading
import webbrowser
import time
import signal
import subprocess
from pathlib import Path
from http.server import HTTPServer, SimpleHTTPRequestHandler
from urllib.parse import urlparse

# Photon WebSocket proxy (forwards WSS to Photon UDP server)
import asyncio
import websockets

class WebGLHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, webgl_dir=None, **kwargs):
        self.webgl_dir = webgl_dir or Path(__file__).parent / "WebGL"
        super().__init__(*args, directory=str(self.webgl_dir), **kwargs)
    
    def end_headers(self):
        # Enable CORS for WebSocket connections
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        super().end_headers()
    
    def do_OPTIONS(self):
        self.send_response(200)
        self.end_headers()

class PhotonWebSocketProxy:
    """Proxies WSS (browser) to Photon UDP (server)"""
    def __init__(self, photon_host="127.0.0.1", photon_port=5055, ws_port=9093):
        self.photon_host = photon_host
        self.photon_port = photon_port
        self.ws_port = ws_port
        self.udp_socket = None
        self.clients = {}
    
    async def start(self):
        # Create UDP socket to Photon
        self.udp_socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.udp_socket.setblocking(False)
        
        # Start WebSocket server
        server = await websockets.serve(
            self.handle_client, 
            "0.0.0.0", 
            self.ws_port,
            ssl=None  # Use HTTPS context from main server
        )
        print(f"Photon WebSocket proxy running on wss://localhost:{self.ws_port}")
        await server.wait_closed()
    
    async def handle_client(self, websocket, path):
        client_id = id(websocket)
        self.clients[client_id] = websocket
        
        # Forward messages
        async for message in websocket:
            if isinstance(message, bytes):
                # Binary: forward to Photon UDP
                self.udp_socket.sendto(message, (self.photon_host, self.photon_port))
            else:
                # Text: handle Photon protocol
                await self.handle_photon_text(client_id, message)
    
    async def handle_photon_text(self, client_id, message):
        # Photon WebSocket protocol handling
        pass

# Simple approach: Just serve WebGL + open browser
# Photon connection handled by WebGL build itself (connects to ws://host:9093)

def find_webgl_build():
    """Find WebGL build directory"""
    possible = [
        Path(__file__).parent / "Builds" / "WebGL",
        Path(__file__).parent / "WebGL",
        Path(sys._MEIPASS) / "WebGL" if hasattr(sys, '_MEIPASS') else None,
    ]
    for p in possible:
        if p and p.exists() and (p / "index.html").exists():
            return p
    return None

def generate_self_signed_cert(cert_path, key_path):
    """Generate self-signed certificate for localhost"""
    from cryptography import x509
    from cryptography.x509.oid import NameOID
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    import datetime
    
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    subject = issuer = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, u"localhost")])
    cert = x509.CertificateBuilder().subject_name(subject).issuer_name(issuer).public_key(key.public_key()).serial_number(
        x509.random_serial_number()).not_valid_before(
        datetime.datetime.utcnow()).not_valid_after(
        datetime.datetime.utcnow() + datetime.timedelta(days=365)).add_extension(
        x509.SubjectAlternativeName([x509.DNSName(u"localhost")]), critical=False).sign(key, hashes.SHA256())
    
    with open(key_path, "wb") as f:
        f.write(key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
    with open(cert_path, "wb") as f:
        f.write(cert.public_bytes(serialization.Encoding.PEM))

def main():
    # Find WebGL build
    webgl_dir = find_webgl_build()
    if not webgl_dir:
        print("ERROR: WebGL build not found!")
        print("Build with: Unity -batchmode -projectPath . -executeMethod BuildScript.BuildWebGL")
        sys.exit(1)
    
    print(f"Found WebGL build: {webgl_dir}")
    
    # Setup SSL cert
    cert_dir = webgl_dir / "server"
    cert_dir.mkdir(exist_ok=True)
    cert_path = cert_dir / "cert.pem"
    key_path = cert_dir / "key.pem"
    
    if not cert_path.exists():
        print("Generating self-signed certificate...")
        generate_self_signed_cert(cert_path, key_path)
    
    # Change to WebGL directory
    os.chdir(webgl_dir)
    
    # Create HTTPS server
    server = HTTPServer(('0.0.0.0', 8443), lambda *args, **kwargs: WebGLHandler(*args, webgl_dir=webgl_dir, **kwargs))
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(cert_path, key_path)
    server.socket = context.wrap_socket(server.socket, server_side=True)
    
    print(f"Server running at https://localhost:8443")
    print(f"Photon WebSocket: ws://localhost:9093 (configure in PhotonServerSettings)")
    print("Press Ctrl+C to stop")
    
    # Open browser
    def open_browser():
        time.sleep(1.5)
        webbrowser.open('https://localhost:8443')
    
    threading.Thread(target=open_browser, daemon=True).start()
    
    # Handle shutdown
    def signal_handler(sig, frame):
        print("\nShutting down...")
        server.shutdown()
        sys.exit(0)
    
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)
    
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()

if __name__ == "__main__":
    main()