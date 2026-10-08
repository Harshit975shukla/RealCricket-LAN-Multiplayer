using UnityEngine;
using UnityEditor;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Editor script to set up the RealCricket LAN multiplayer scene
/// </summary>
public class LANSceneSetup : EditorWindow
{
    [MenuItem("RealCricket LAN/Setup Scene")]
    public static void ShowWindow()
    {
        GetWindow<LANSceneSetup>("LAN Scene Setup");
    }
    
    private void OnGUI()
    {
        GUILayout.Label("RealCricket LAN Multiplayer Scene Setup", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        if (GUILayout.Button("Create Network Manager", GUILayout.Height(30)))
        {
            CreateNetworkManager();
        }
        
        if (GUILayout.Button("Create Player Prefab", GUILayout.Height(30)))
        {
            CreatePlayerPrefab();
        }
        
        if (GUILayout.Button("Create Ball Prefab", GUILayout.Height(30)))
        {
            CreateBallPrefab();
        }
        
        if (GUILayout.Button("Create Cricket Match Scene", GUILayout.Height(30)))
        {
            CreateMatchScene();
        }
        
        GUILayout.Space(20);
        GUILayout.Label("Manual Setup Steps:", EditorStyles.boldLabel);
        GUILayout.Label("1. Install Photon PUN 2 from Asset Store");
        GUILayout.Label("2. Run 'Create Network Manager' button");
        GUILayout.Label("3. Run 'Create Player Prefab' button");
        GUILayout.Label("4. Run 'Create Ball Prefab' button");
        GUILayout.Label("5. Run 'Create Cricket Match Scene' button");
        GUILayout.Label("6. Configure PhotonServerSettings with your LAN IP");
        GUILayout.Label("7. Build and run on multiple machines");
    }
    
    private static void CreateNetworkManager()
    {
        GameObject networkManager = new GameObject("LANNetworkManager");
        networkManager.AddComponent<LANNetworkManager>();
        
        // Add PhotonView for RPCs
        PhotonView pv = networkManager.AddComponent<PhotonView>();
        pv.ViewID = 1; // Fixed ID for network manager
        
        // Ensure it persists
        UnityEngine.Object.DontDestroyOnLoad(networkManager);
        
        Debug.Log("[LANSceneSetup] Created LANNetworkManager");
    }
    
    private static void CreatePlayerPrefab()
    {
        GameObject player = new GameObject("Player");
        
        // Add components
        player.AddComponent<CharacterController>();
        player.AddComponent<Rigidbody>().useGravity = false;
        player.AddComponent<Animator>();
        
        // Photon components
        PhotonView pv = player.AddComponent<PhotonView>();
        pv.ObservedComponents = new System.Collections.Generic.List<Component>();
        
        PhotonTransformView transformView = player.AddComponent<PhotonTransformView>();
        transformView.m_SynchronizePosition = true;
        transformView.m_SynchronizeRotation = true;
        transformView.m_SynchronizeScale = false;
        transformView.m_InterpolateOption = PhotonTransformViewInterpolateOptions.Lerp;
        transformView.m_InterpolateLerpSpeed = 10f;
        pv.ObservedComponents.Add(transformView);
        
        // Player controller
        PlayerController controller = player.AddComponent<PlayerController>();
        
        // Create child objects for visual representation
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(player.transform);
        body.transform.localPosition = new Vector3(0, 1, 0);
        body.transform.localScale = new Vector3(0.5f, 1.8f, 0.5f);
        
        // Bat
        GameObject bat = new GameObject("Bat");
        bat.transform.SetParent(player.transform);
        bat.transform.localPosition = new Vector3(0.3f, 1.5f, 0.2f);
        bat.transform.localRotation = Quaternion.Euler(0, 90, 0);
        bat.transform.localScale = new Vector3(0.05f, 1f, 0.05f);
        GameObject batMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        batMesh.transform.SetParent(bat.transform);
        batMesh.transform.localPosition = Vector3.zero;
        batMesh.transform.localScale = new Vector3(1, 1, 1);
        
        // Ball release point
        GameObject releasePoint = new GameObject("BallReleasePoint");
        releasePoint.transform.SetParent(player.transform);
        releasePoint.transform.localPosition = new Vector3(0.3f, 1.8f, 0.5f);
        releasePoint.transform.localRotation = Quaternion.identity;
        
        // Set references via SerializedObject
        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("batTransform").objectReferenceValue = bat.transform;
        so.FindProperty("ballReleasePoint").objectReferenceValue = releasePoint.transform;
        so.ApplyModifiedProperties();
        
        // Save as prefab
        string prefabPath = "Assets/Prefabs/Player.prefab";
        PrefabUtility.SaveAsPrefabAsset(player, prefabPath);
        GameObject.DestroyImmediate(player);
        
        Debug.Log($"[LANSceneSetup] Created Player prefab at {prefabPath}");
    }
    
    private static void CreateBallPrefab()
    {
        GameObject ball = new GameObject("CricketBall");
        
        // Physics
        Rigidbody rb = ball.AddComponent<Rigidbody>();
        rb.mass = 0.156f;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        
        SphereCollider collider = ball.AddComponent<SphereCollider>();
        collider.radius = 0.036f; // Cricket ball radius ~3.6cm
        collider.material = new PhysicMaterial("BallPhysics") 
        { 
            bounciness = 0.6f, 
            dynamicFriction = 0.4f, 
            staticFriction = 0.4f,
            bounceCombine = PhysicMaterialCombine.Maximum,
            frictionCombine = PhysicMaterialCombine.Average
        };
        
        // Photon components
        PhotonView pv = ball.AddComponent<PhotonView>();
        PhotonRigidbodyView rigidbodyView = ball.AddComponent<PhotonRigidbodyView>();
        rigidbodyView.m_SynchronizePosition = true;
        rigidbodyView.m_SynchronizeRotation = true;
        rigidbodyView.m_SynchronizeVelocity = true;
        rigidbodyView.m_SynchronizeAngularVelocity = true;
        rigidbodyView.m_TeleportEnabled = true;
        rigidbodyView.m_TeleportIfDistanceGreaterThan = 1f;
        pv.ObservedComponents.Add(rigidbodyView);
        
        // Ball controller
        BallController ballController = ball.AddComponent<BallController>();
        
        // Visual
        GameObject ballMesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ballMesh.name = "BallMesh";
        ballMesh.transform.SetParent(ball.transform);
        ballMesh.transform.localPosition = Vector3.zero;
        ballMesh.transform.localScale = Vector3.one * 0.072f; // Diameter ~7.2cm
        
        // Red cricket ball material
        Material ballMat = new Material(Shader.Find("Standard"));
        ballMat.color = new Color(0.8f, 0.1f, 0.1f);
        ballMat.SetFloat("_Glossiness", 0.5f);
        ballMesh.GetComponent<Renderer>().material = ballMat;
        
        // Save as prefab
        string prefabPath = "Assets/Prefabs/CricketBall.prefab";
        PrefabUtility.SaveAsPrefabAsset(ball, prefabPath);
        GameObject.DestroyImmediate(ball);
        
        Debug.Log($"[LANSceneSetup] Created CricketBall prefab at {prefabPath}");
    }
    
    private static void CreateMatchScene()
    {
        // Create new scene
        string scenePath = "Assets/Scenes/CricketMatch.unity";
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        
        // Create ground
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "CricketGround";
        ground.transform.localScale = new Vector3(50, 1, 50); // Large cricket field
        
        // Ground material
        Material groundMat = new Material(Shader.Find("Standard"));
        groundMat.color = new Color(0.2f, 0.6f, 0.2f); // Green grass
        ground.GetComponent<Renderer>().material = groundMat;
        
        // Add pitch (rectangle in center)
        GameObject pitch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pitch.name = "Pitch";
        pitch.transform.position = new Vector3(0, 0.01f, 0);
        pitch.transform.localScale = new Vector3(3.05f, 0.02f, 20.12f); // 22 yards = 20.12m
        Material pitchMat = new Material(Shader.Find("Standard"));
        pitchMat.color = new Color(0.9f, 0.8f, 0.6f); // Sandy pitch color
        pitch.GetComponent<Renderer>().material = pitchMat;
        
        // Create stumps at both ends
        CreateStumps(new Vector3(0, 0.35f, 10.06f)); // Bowling end
        CreateStumps(new Vector3(0, 0.35f, -10.06f)); // Batting end
        
        // Create boundary rope
        CreateBoundary();
        
        // Add lighting
        GameObject sun = new GameObject("Directional Light");
        Light sunLight = sun.AddComponent<Light>();
        sunLight.type = LightType.Directional;
        sunLight.intensity = 1.2f;
        sunLight.color = Color.white;
        sun.transform.rotation = Quaternion.Euler(50, -30, 0);
        
        // Add camera
        GameObject camera = new GameObject("Main Camera");
        Camera cam = camera.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 15, -15);
        camera.transform.rotation = Quaternion.Euler(30, 0, 0);
        
        // Add Network Manager
        CreateNetworkManager();
        
        // Add Game Manager
        GameObject gameManager = new GameObject("CricketGameManager");
        gameManager.AddComponent<CricketGameManager>();
        
        // Save scene
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
        
        Debug.Log($"[LANSceneSetup] Created CricketMatch scene at {scenePath}");
    }
    
    private static void CreateStumps(Vector3 position)
    {
        GameObject stumps = new GameObject("Stumps");
        stumps.transform.position = position;
        
        // Three stumps
        for (int i = 0; i < 3; i++)
        {
            GameObject stump = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stump.name = $"Stump_{i}";
            stump.transform.SetParent(stumps.transform);
            stump.transform.localPosition = new Vector3((i - 1) * 0.09f, 0.35f, 0); // 9 inches apart
            stump.transform.localScale = new Vector3(0.038f, 0.71f, 0.038f); // Cricket stump dimensions
            
            Material stumpMat = new Material(Shader.Find("Standard"));
            stumpMat.color = new Color(0.9f, 0.85f, 0.7f); // Wood color
            stump.GetComponent<Renderer>().material = stumpMat;
        }
        
        // Bails
        for (int i = 0; i < 2; i++)
        {
            GameObject bail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bail.name = $"Bail_{i}";
            bail.transform.SetParent(stumps.transform);
            bail.transform.localPosition = new Vector3((i - 0.5f) * 0.09f, 0.71f, 0);
            bail.transform.localScale = new Vector3(0.11f, 0.013f, 0.038f);
            
            Material bailMat = new Material(Shader.Find("Standard"));
            bailMat.color = new Color(0.9f, 0.85f, 0.7f);
            bail.GetComponent<Renderer>().material = bailMat;
        }
    }
    
    private static void CreateBoundary()
    {
        GameObject boundary = new GameObject("Boundary");
        
        // Create boundary rope as a circle of cylinders
        int segments = 64;
        float radius = 65f; // ~65m boundary
        
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2;
            float nextAngle = (float)(i + 1) / segments * Mathf.PI * 2;
            
            Vector3 start = new Vector3(Mathf.Cos(angle) * radius, 0.1f, Mathf.Sin(angle) * radius);
            Vector3 end = new Vector3(Mathf.Cos(nextAngle) * radius, 0.1f, Mathf.Sin(nextAngle) * radius);
            
            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            segment.name = $"BoundarySegment_{i}";
            segment.transform.SetParent(boundary.transform);
            
            Vector3 mid = (start + end) / 2;
            float length = Vector3.Distance(start, end);
            segment.transform.position = mid;
            segment.transform.up = (end - start).normalized;
            segment.transform.localScale = new Vector3(0.1f, length / 2, 0.1f);
            
            Material boundaryMat = new Material(Shader.Find("Standard"));
            boundaryMat.color = Color.white;
            segment.GetComponent<Renderer>().material = boundaryMat;
        }
    }
}