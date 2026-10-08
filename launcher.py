# launcher.py - Single-file launcher that serves the WebGL build and opens the browser
# Package as .exe with:
#   pyinstaller --onefile --noconsole --add-data "Builds/WebGL;WebGL" launcher.py
#
# http://localhost is a secure context in all modern browsers, so plain HTTP
# avoids self-signed-certificate warnings and mixed-content blocking.

import os
import sys
import threading
import time
import webbrowser
import socket
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from pathlib import Path

PORT = 8443


def find_webgl_build():
    """Locate the WebGL build directory (works both from source and frozen exe)."""
    candidates = []

    # PyInstaller onefile: assets extracted to sys._MEIPASS
    if hasattr(sys, "_MEIPASS"):
        candidates.append(Path(sys._MEIPASS) / "WebGL")

    # Running from source
    here = Path(__file__).parent
    candidates.append(here / "Builds" / "WebGL")
    candidates.append(here / "WebGL")

    # Launched from a different cwd (e.g. double-clicked exe next to Builds/)
    candidates.append(Path.cwd() / "Builds" / "WebGL")
    candidates.append(Path.cwd() / "WebGL")

    for p in candidates:
        try:
            if (p / "index.html").exists():
                return p.resolve()
        except OSError:
            continue
    return None


class WebGLHandler(SimpleHTTPRequestHandler):
    """Serves the Unity WebGL build, handling Unity's pre-compressed .gz files.

    Unity (with Gzip compression enabled) emits *.gz files that the loader
    fetches *as-is* (e.g. Build/WebGL.framework.js.gz). The browser will only
    decompress them if the server responds with 'Content-Encoding: gzip',
    otherwise Unity throws 'Unable to parse Build/WebGL.framework.js.gz!'.
    """

    def end_headers(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def send_head(self):
        # Unity's loader requests the .gz file directly; serve it with the
        # gzip Content-Encoding header instead of letting SimpleHTTP serve
        # it as an opaque binary download.
        path = self.translate_path(self.path)
        if path.endswith(".gz") and os.path.isfile(path):
            self.send_response(200)
            self.send_header("Content-Type", self.guess_type(path[:-3]))
            self.send_header("Content-Encoding", "gzip")
            self.send_header("Content-Length", str(os.path.getsize(path)))
            self.end_headers()
            return open(path, "rb")
        return super().send_head()

    def log_message(self, fmt, *args):
        pass  # keep console clean in --noconsole mode


def port_in_use(port):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(0.5)
        return s.connect_ex(("127.0.0.1", port)) == 0


def main():
    webgl_dir = find_webgl_build()
    if not webgl_dir:
        print("ERROR: WebGL build not found.")
        print("Build it first with:")
        print('  Unity -batchmode -projectPath . -executeMethod BuildScript.BuildWebGL -quit')
        input("Press Enter to exit...")
        sys.exit(1)

    # If port busy, assume a previous launcher instance is already serving.
    url = f"http://localhost:{PORT}"
    if not port_in_use(PORT):
        os.chdir(webgl_dir)
        server = ThreadingHTTPServer(("0.0.0.0", PORT), WebGLHandler)
        server.daemon_threads = True
        threading.Thread(target=server.serve_forever, daemon=True).start()
        print(f"Serving {webgl_dir} at {url} (LAN: http://<your-ip>:{PORT})")

    def open_browser():
        time.sleep(1.0)
        webbrowser.open(url)

    threading.Thread(target=open_browser, daemon=True).start()

    # Keep the process alive; Ctrl+C stops it.
    try:
        while True:
            time.sleep(3600)
    except KeyboardInterrupt:
        print("\nShutting down...")


if __name__ == "__main__":
    main()
