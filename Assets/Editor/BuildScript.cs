#!/bin/bash
# build_webgl.sh - Run this in Unity's batch mode after installing Unity
# Usage: /path/to/Unity -batchmode -projectPath . -executeMethod BuildScript.BuildWebGL -quit

using UnityEngine;
using UnityEditor;
using System.IO;

public class BuildScript
{
    public static void BuildWebGL()
    {
        string buildPath = "Builds/WebGL";
        
        // Ensure PhotonServerSettings for WebGL (WebSocket)
        var settings = Resources.Load<Photon.Pun.PhotonServerSettings>("PhotonServerSettings");
        if (settings != null)
        {
            settings.AppSettings.Protocol = ExitGames.Client.Photon.ConnectionProtocol.WebSocketSecure;
            settings.AppSettings.Port = 9093; // WebSocket secure port
            settings.AppSettings.Server = "localhost"; // Will be replaced at runtime
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
        
        // Build Player
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/CricketMatch.unity" },
            locationPathName = buildPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };
        
        var report = BuildPipeline.BuildPlayer(options);
        
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"WebGL build successful: {buildPath}");
            
            // Generate server bundle
            GenerateServerBundle(buildPath);
        }
        else
        {
            Debug.LogError("WebGL build failed!");
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error) Debug.LogError(msg.content);
                }
            }
        }
    }
    
    static void GenerateServerBundle(string webglPath)
    {
        // Copy server files to build output
        string serverSrc = "Server";
        string serverDst = Path.Combine(webglPath, "server");
        
        if (Directory.Exists(serverSrc))
        {
            CopyDirectory(serverSrc, serverDst);
        }
        
        // Create launch script
        string launchScript = @"
#!/bin/bash
cd ""$(dirname ""$0"")/server""
# Generate self-signed cert if needed
if [ ! -f cert.pem ]; then
    openssl req -x509 -newkey rsa:2048 -keyout key.pem -out cert.pem -days 365 -nodes -subj ""/CN=localhost""
fi
# Start HTTPS server
python3 -c "
import http.server, ssl, os, threading, webbrowser, time
os.chdir('..')
server = http.server.HTTPServer(('0.0.0.0', 8443), http.server.SimpleHTTPRequestHandler)
server.socket = ssl.wrap_socket(server.socket, keyfile='server/key.pem', certfile='server/cert.pem', server_side=True)
print('Server running at https://localhost:8443')
threading.Thread(target=lambda: (time.sleep(1), webbrowser.open('https://localhost:8443'))).start()
server.serve_forever()
"
"";
        
        File.WriteAllText(Path.Combine(webglPath, "launch.sh"), launchScript);
        File.WriteAllText(Path.Combine(webglPath, "launch.bat"), launchScript.Replace("#!/bin/bash", "@echo off").Replace("python3", "python"));
    }
    
    static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string file in Directory.GetFiles(src))
        {
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        }
        foreach (string dir in Directory.GetDirectories(src))
        {
            CopyDirectory(dir, Path.Combine(dst, Path.GetDirectoryName(dir)));
        }
    }
}