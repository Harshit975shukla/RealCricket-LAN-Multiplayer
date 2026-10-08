using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

#if PHOTON_PUN_2 || PHOTON_REALTIME
using Photon.Pun;
using Photon.Realtime;
#endif

/// <summary>
/// Editor script to set up the RealCricket LAN multiplayer scene
/// </summary>
public class LANSceneSetup : EditorWindow
{
    [MenuItem("RealCricket LAN/Setup Scene Window")]
    public static void ShowWindow()
    {
        GetWindow<LANSceneSetup>("LAN Scene Setup");
    }
    
    [MenuItem("RealCricket LAN/Generate All (Prefabs & Scene)")]
    public static void SetupAll()
    {
        CreatePlayerPrefab();
        CreateBallPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        CreateMatchScene();
        Debug.Log("[LANSceneSetup] Generated Player prefab, Ball prefab, and CricketMatch scene.");
    }
    
    private void OnGUI()
    {
        GUILayout.Label("RealCricket LAN Multiplayer Scene Setup", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
#if !PHOTON_PUN_2 && !PHOTON_REALTIME
        EditorGUILayout.HelpBox("Photon PUN 2 is not detected. Multi-device networking requires Photon PUN 2 (install from Asset Store). Local scene and prefabs can still be generated.", MessageType.Info);
        GUILayout.Space(10);
#endif
        
        if (GUILayout.Button("Generate All (Recommended)", GUILayout.Height(35)))
        {
            SetupAll();
        }
        
        GUILayout.Space(5);
        
        if (GUILayout.Button("Create Network Manager", GUILayout.Height(25)))
        {
            CreateNetworkManager();
        }
        
        if (GUILayout.Button("Create Player Prefab", GUILayout.Height(25)))
        {
            CreatePlayerPrefab();
        }
        
        if (GUILayout.Button("Create Ball Prefab", GUILayout.Height(25)))
        {
            CreateBallPrefab();
        }
        
        if (GUILayout.Button("Create Cricket Match Scene", GUILayout.Height(25)))
        {
            CreateMatchScene();
        }
        
        GUILayout.Space(20);
        GUILayout.Label("Manual Setup Steps:", EditorStyles.boldLabel);
        GUILayout.Label("1. Install Photon PUN 2 from Asset Store (for LAN multiplayer)");
        GUILayout.Label("2. Run 'Generate All' button");
        GUILayout.Label("3. Configure PhotonServerSettings with your LAN IP");
        GUILayout.Label("4. Build and run on multiple machines");
    }
    
    public static GameObject CreateNetworkManager()
    {
        GameObject networkManager = new GameObject("LANNetworkManager");
        networkManager.AddComponent<LANNetworkManager>();
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
        // Add PhotonView for RPCs
        PhotonView pv = networkManager.AddComponent<PhotonView>();
        pv.ViewID = 1; // Fixed ID for network manager
#endif
        
        // Ensure it persists in play mode
        if (Application.isPlaying)
        {
            UnityEngine.Object.DontDestroyOnLoad(networkManager);
        }
        
        Debug.Log("[LANSceneSetup] Created LANNetworkManager");
        return networkManager;
    }
    
    public static void CreatePlayerPrefab()
    {
        GameObject player = new GameObject("Player");
        
        // Add components
        player.AddComponent<CharacterController>();
        player.AddComponent<Rigidbody>().useGravity = false;
        player.AddComponent<Animator>();
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
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
#endif
        
        // Player controller
        PlayerController controller = player.AddComponent<PlayerController>();
        
        // Create child objects for visual representation
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(player.transform);
        body.transform.localPosition = new Vector3(0, 1, 0);
        body.transform.localScale = new Vector3(0.5f, 1.8f, 0.5f);
        Material bodyMat = new Material(Shader.Find("Standard"));
        bodyMat.color = new Color(0.12f, 0.45f, 0.88f); // Team Blue Jersey
        body.GetComponent<Renderer>().material = bodyMat;
        
        // Helmet on head
        GameObject helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        helmet.name = "Helmet";
        helmet.transform.SetParent(player.transform);
        helmet.transform.localPosition = new Vector3(0, 1.82f, 0);
        helmet.transform.localScale = new Vector3(0.38f, 0.38f, 0.38f);
        Material helmetMat = new Material(Shader.Find("Standard"));
        helmetMat.color = new Color(0.05f, 0.16f, 0.38f); // Deep Navy Helmet
        helmet.GetComponent<Renderer>().material = helmetMat;

        // Face grill on helmet
        GameObject grill = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        grill.name = "Grill";
        grill.transform.SetParent(player.transform);
        grill.transform.localPosition = new Vector3(0, 1.72f, 0.16f);
        grill.transform.localRotation = Quaternion.Euler(90, 0, 0);
        grill.transform.localScale = new Vector3(0.18f, 0.04f, 0.14f);
        Material grillMat = new Material(Shader.Find("Standard"));
        grillMat.color = new Color(0.75f, 0.75f, 0.78f); // Steel grill
        grill.GetComponent<Renderer>().material = grillMat;

        // White Batting Pads (Leg guards)
        Material padMat = new Material(Shader.Find("Standard"));
        padMat.color = new Color(0.96f, 0.96f, 0.96f); // Clean white batting pads
        
        GameObject leftPad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftPad.name = "LeftPad";
        leftPad.transform.SetParent(player.transform);
        leftPad.transform.localPosition = new Vector3(-0.15f, 0.42f, 0.06f);
        leftPad.transform.localScale = new Vector3(0.17f, 0.78f, 0.16f);
        leftPad.GetComponent<Renderer>().material = padMat;

        GameObject rightPad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightPad.name = "RightPad";
        rightPad.transform.SetParent(player.transform);
        rightPad.transform.localPosition = new Vector3(0.15f, 0.42f, 0.06f);
        rightPad.transform.localScale = new Vector3(0.17f, 0.78f, 0.16f);
        rightPad.GetComponent<Renderer>().material = padMat;
        
        // Bat: use CC0 cricket bat model if available, else cylinder primitive
        GameObject bat = new GameObject("Bat");
        bat.transform.SetParent(player.transform);
        bat.transform.localPosition = new Vector3(0.35f, 0.75f, 0.35f);
        bat.transform.localRotation = Quaternion.Euler(15, -20, 10);
        bat.transform.localScale = Vector3.one * 0.45f; // typical FBX scale for these packs

        GameObject batModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CricketModels/cricket_bat_1.fbx");
        Texture2D batTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CricketModels/Textures/bat_1.png");
        if (batModel != null)
        {
            GameObject batMesh = (GameObject)PrefabUtility.InstantiatePrefab(batModel);
            batMesh.transform.SetParent(bat.transform);
            batMesh.transform.localPosition = Vector3.zero;
            batMesh.transform.localRotation = Quaternion.identity;
            // Normalise bat to ~0.85m tall in hand
            Renderer batRend = batMesh.GetComponentInChildren<Renderer>();
            if (batRend != null)
            {
                float batHeight = batRend.bounds.size.y;
                if (batHeight > 0.001f) batMesh.transform.localScale *= (0.85f / batHeight);
            }
            if (batTex != null)
            {
                foreach (Renderer r in batMesh.GetComponentsInChildren<Renderer>())
                {
                    Material m = new Material(Shader.Find("Standard"));
                    m.mainTexture = batTex;
                    r.material = m;
                }
            }
        }
        else
        {
            GameObject batMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            batMesh.transform.SetParent(bat.transform);
            batMesh.transform.localPosition = Vector3.zero;
            batMesh.transform.localScale = Vector3.one;
            Material batMat = new Material(Shader.Find("Standard"));
            batMat.color = new Color(0.85f, 0.72f, 0.52f); // Willow wood
            batMesh.GetComponent<Renderer>().material = batMat;
        }
        
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
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        PrefabUtility.SaveAsPrefabAsset(player, prefabPath);
        GameObject.DestroyImmediate(player);
        
        Debug.Log($"[LANSceneSetup] Created Player prefab at {prefabPath}");
    }
    
    public static void CreateBallPrefab()
    {
        GameObject ball = new GameObject("CricketBall");
        ball.tag = "Ball";
        
        // Physics
        Rigidbody rb = ball.AddComponent<Rigidbody>();
        rb.mass = 0.156f;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        
        SphereCollider collider = ball.AddComponent<SphereCollider>();
        collider.radius = 0.036f; // Cricket ball radius ~3.6cm
        
        // Ball controller
        ball.AddComponent<BallController>();
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
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
#endif
        
        // Visual: use the CC0 cricket ball model with stitched texture if available,
        // fall back to a primitive sphere otherwise.
        GameObject ballMesh;
        GameObject ballModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CricketModels/cricket_ball_new.fbx");
        Texture2D ballTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CricketModels/Textures/ball_new.png");
        if (ballModel != null)
        {
            ballMesh = (GameObject)PrefabUtility.InstantiatePrefab(ballModel);
            // FBX import can scale wildly; normalise to cricket ball size (7.2cm diameter)
            float baseSize = ballMesh.GetComponentInChildren<Renderer>().bounds.size.y;
            if (baseSize > 0.001f) ballMesh.transform.localScale = Vector3.one * (0.072f / baseSize);
        }
        else
        {
            ballMesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballMesh.transform.localScale = Vector3.one * 0.072f;
        }
        ballMesh.name = "BallMesh";
        ballMesh.transform.SetParent(ball.transform);
        ballMesh.transform.localPosition = Vector3.zero;
        ballMesh.transform.localRotation = Quaternion.identity;

        // Apply stitched-leather texture if available
        if (ballTex != null)
        {
            foreach (Renderer r in ballMesh.GetComponentsInChildren<Renderer>())
            {
                Material m = new Material(Shader.Find("Standard"));
                m.mainTexture = ballTex;
                m.SetFloat("_Glossiness", 0.6f);
                r.material = m;
            }
        }
        else
        {
            Material ballMat = new Material(Shader.Find("Standard"));
            ballMat.color = new Color(0.85f, 0.12f, 0.12f);
            ballMat.SetFloat("_Glossiness", 0.6f);
            foreach (Renderer r in ballMesh.GetComponentsInChildren<Renderer>()) r.material = ballMat;
        }

        // Strip any colliders the FBX brought in (root owns the SphereCollider)
        foreach (Collider c in ballMesh.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        
        // Save as prefab
        string prefabPath = "Assets/Prefabs/CricketBall.prefab";
        System.IO.Directory.CreateDirectory("Assets/Prefabs");
        PrefabUtility.SaveAsPrefabAsset(ball, prefabPath);
        GameObject.DestroyImmediate(ball);
        
        Debug.Log($"[LANSceneSetup] Created CricketBall prefab at {prefabPath}");
    }
    
    public static void CreateMatchScene()
    {
        string scenePath = "Assets/Scenes/CricketMatch.unity";
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        // Start with clean empty scene (no duplicate camera or light!)
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        
        // 1. Single Directional Sunlight with Soft Shadows
        GameObject sun = new GameObject("Directional Light");
        Light sunLight = sun.AddComponent<Light>();
        sunLight.type = LightType.Directional;
        sunLight.intensity = 1.35f;
        sunLight.color = new Color(1f, 0.98f, 0.92f);
        sunLight.shadows = LightShadows.Soft;
        sunLight.shadowStrength = 0.85f;
        sun.transform.rotation = Quaternion.Euler(45, -35, 0);

        // 2. Single Broadcast Batting Camera with Daylight Sky & AudioListener
        GameObject camera = new GameObject("Main Camera");
        Camera cam = camera.AddComponent<Camera>();
        camera.AddComponent<AudioListener>();
        camera.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.48f, 0.72f, 0.96f); // Vivid clear sky blue
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 1000f;
        cam.fieldOfView = 50f;
        camera.transform.position = new Vector3(0f, 3.2f, -15.5f);
        camera.transform.rotation = Quaternion.Euler(11f, 0, 0);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.72f, 0.76f, 0.82f);

        // 3. Outfield Grass Field (Large green stadium turf)
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "CricketOutfield";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(16, 1, 16); // 160m x 160m
        Material groundMat = new Material(Shader.Find("Standard"));
        groundMat.color = new Color(0.18f, 0.52f, 0.18f); // Rich grass green
        ground.GetComponent<Renderer>().material = groundMat;

        // Infield Circle (30-yard circle disk)
        GameObject infield = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        infield.name = "InfieldCircle";
        infield.transform.position = new Vector3(0, 0.015f, 0);
        infield.transform.localScale = new Vector3(55f, 0.01f, 55f);
        Material infieldMat = new Material(Shader.Find("Standard"));
        infieldMat.color = new Color(0.24f, 0.60f, 0.24f); // Light mowed turf
        infield.GetComponent<Renderer>().material = infieldMat;
        Collider infieldCol = infield.GetComponent<Collider>();
        if (infieldCol != null) Object.DestroyImmediate(infieldCol);

        // 4. Regulation 22-Yard Cricket Pitch (Sandy Clay) - use CC0 pitch model if available
        GameObject pitch;
        GameObject pitchModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CricketModels/cricket_pitch_1.fbx");
        Texture2D pitchTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CricketModels/Textures/pitch_1.png");
        if (pitchModel != null)
        {
            pitch = (GameObject)PrefabUtility.InstantiatePrefab(pitchModel);
            pitch.name = "Pitch";
            pitch.transform.position = new Vector3(0, 0.03f, 0);
            Renderer pr = pitch.GetComponentInChildren<Renderer>();
            if (pr != null)
            {
                // Normalise to regulation 3.05m x 20.12m strip
                Vector3 sz = pr.bounds.size;
                if (sz.z > 0.001f) pitch.transform.localScale = new Vector3(3.2f / sz.x, 1f, 20.12f / sz.z);
            }
            if (pitchTex != null)
            {
                foreach (Renderer rend in pitch.GetComponentsInChildren<Renderer>())
                {
                    Material m = new Material(Shader.Find("Standard"));
                    m.mainTexture = pitchTex;
                    rend.material = m;
                }
            }
            foreach (Collider c in pitch.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }
        else
        {
            pitch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pitch.name = "Pitch";
            pitch.transform.position = new Vector3(0, 0.03f, 0);
            pitch.transform.localScale = new Vector3(3.2f, 0.04f, 20.12f);
            Material pitchMat = new Material(Shader.Find("Standard"));
            pitchMat.color = new Color(0.85f, 0.76f, 0.55f); // Warm clay pitch
            pitch.GetComponent<Renderer>().material = pitchMat;
        }
        
        // Crease markings (Crisp White Chalk Lines)
        Material creaseMat = new Material(Shader.Find("Standard"));
        creaseMat.color = Color.white;
        
        // Batting creases
        GameObject popCreaseBat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        popCreaseBat.name = "PoppingCrease_Batting";
        popCreaseBat.transform.position = new Vector3(0, 0.055f, -8.84f);
        popCreaseBat.transform.localScale = new Vector3(2.8f, 0.01f, 0.1f);
        popCreaseBat.GetComponent<Renderer>().material = creaseMat;
        Collider cBatCol = popCreaseBat.GetComponent<Collider>();
        if (cBatCol != null) Object.DestroyImmediate(cBatCol);
        
        GameObject bowlCreaseBat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bowlCreaseBat.name = "BowlingCrease_Batting";
        bowlCreaseBat.transform.position = new Vector3(0, 0.055f, -10.06f);
        bowlCreaseBat.transform.localScale = new Vector3(2.8f, 0.01f, 0.1f);
        bowlCreaseBat.GetComponent<Renderer>().material = creaseMat;
        Collider bBatCol = bowlCreaseBat.GetComponent<Collider>();
        if (bBatCol != null) Object.DestroyImmediate(bBatCol);

        // Bowling creases
        GameObject popCreaseBowl = GameObject.CreatePrimitive(PrimitiveType.Cube);
        popCreaseBowl.name = "PoppingCrease_Bowling";
        popCreaseBowl.transform.position = new Vector3(0, 0.055f, 8.84f);
        popCreaseBowl.transform.localScale = new Vector3(2.8f, 0.01f, 0.1f);
        popCreaseBowl.GetComponent<Renderer>().material = creaseMat;
        Collider cBowlCol = popCreaseBowl.GetComponent<Collider>();
        if (cBowlCol != null) Object.DestroyImmediate(cBowlCol);

        GameObject bowlCreaseBowl = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bowlCreaseBowl.name = "BowlingCrease_Bowling";
        bowlCreaseBowl.transform.position = new Vector3(0, 0.055f, 10.06f);
        bowlCreaseBowl.transform.localScale = new Vector3(2.8f, 0.01f, 0.1f);
        bowlCreaseBowl.GetComponent<Renderer>().material = creaseMat;
        Collider bBowlCol = bowlCreaseBowl.GetComponent<Collider>();
        if (bBowlCol != null) Object.DestroyImmediate(bBowlCol);
        
        // 5. Stumps at both ends
        CreateStumps(new Vector3(0, 0.35f, 10.06f)); // Bowling end
        CreateStumps(new Vector3(0, 0.35f, -10.06f)); // Batting end
        
        // 6. White Sight Screens at both ends
        CreateSightScreen(new Vector3(0, 3.5f, 30f)); // Behind bowler
        CreateSightScreen(new Vector3(0, 3.5f, -30f)); // Behind batsman

        // 7. Circular Boundary Rope
        CreateBoundary();

        // 8. Stadium Grandstands (Surrounding seating bowl)
        CreateStadiumStands();

        // 9. Stadium Floodlight Towers
        CreateFloodlightTower(new Vector3(50, 15, 50));
        CreateFloodlightTower(new Vector3(-50, 15, 50));
        CreateFloodlightTower(new Vector3(50, 15, -50));
        CreateFloodlightTower(new Vector3(-50, 15, -50));
        
        // 10. Ball spawn point
        GameObject ballSpawn = new GameObject("BallSpawnPoint");
        ballSpawn.transform.position = new Vector3(0, 1.8f, 9.5f);
        
        // 11. Network Manager & Game Manager
        GameObject networkManager = CreateNetworkManager();
        GameObject gameManager = new GameObject("CricketGameManager");
        CricketGameManager cgm = gameManager.AddComponent<CricketGameManager>();

        // 11b. Broadcast camera (replaces static cam view; keep original cam disabled)
        GameObject camGO = GameObject.Find("Main Camera");
        if (camGO != null)
        {
            BroadcastCamera bcam = camGO.GetComponent<BroadcastCamera>();
            if (bcam == null) bcam = camGO.AddComponent<BroadcastCamera>();
        }

        // 11c. AI fielders at classic cricket positions (ring + deep)
        CreateFielders();
        
        // Load prefabs
        GameObject ballPrefabObj = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CricketBall.prefab");
        GameObject playerPrefabObj = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");

        // 12. Instantiate Batsman at batting crease
        GameObject batsman = null;
        if (playerPrefabObj != null)
        {
            batsman = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefabObj);
            batsman.name = "Batsman";
            batsman.transform.position = new Vector3(0.18f, 0, -9.5f);
            batsman.transform.rotation = Quaternion.Euler(0, 0, 0);
            PlayerController batsmanCtrl = batsman.GetComponent<PlayerController>();
            if (batsmanCtrl != null)
            {
                batsmanCtrl.SetRole(batting: true, bowling: false, fielding: false);
            }
        }

        // 13. Instantiate Bowler at bowling mark
        GameObject bowler = null;
        if (playerPrefabObj != null)
        {
            bowler = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefabObj);
            bowler.name = "Bowler";
            bowler.transform.position = new Vector3(0, 0, 9.5f);
            bowler.transform.rotation = Quaternion.Euler(0, 180, 0);
            PlayerController bowlerCtrl = bowler.GetComponent<PlayerController>();
            if (bowlerCtrl != null)
            {
                bowlerCtrl.SetRole(batting: false, bowling: true, fielding: false);
            }
            // Distinct Gold/Yellow jersey for Bowler
            Transform bBody = bowler.transform.Find("Body");
            if (bBody != null)
            {
                Material goldMat = new Material(Shader.Find("Standard"));
                goldMat.color = new Color(0.96f, 0.78f, 0.10f); // Gold Jersey
                bBody.GetComponent<Renderer>().material = goldMat;
            }
        }

        // 14. Instantiate Wicketkeeper behind batsman stumps
        if (playerPrefabObj != null)
        {
            GameObject keeper = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefabObj);
            keeper.name = "Wicketkeeper";
            keeper.transform.position = new Vector3(0, 0, -12.5f);
            keeper.transform.rotation = Quaternion.Euler(0, 0, 0);
            PlayerController keeperCtrl = keeper.GetComponent<PlayerController>();
            if (keeperCtrl != null)
            {
                keeperCtrl.SetRole(batting: false, bowling: false, fielding: true);
            }
        }

        // 15. Instantiate CricketBall in scene
        GameObject ballInstance = null;
        if (ballPrefabObj != null)
        {
            ballInstance = (GameObject)PrefabUtility.InstantiatePrefab(ballPrefabObj);
            ballInstance.name = "CricketBall";
            ballInstance.transform.position = ballSpawn.transform.position;
            ballInstance.tag = "Ball";
        }
        
        // Wire up serialized references
        SerializedObject gmSo = new SerializedObject(cgm);
        gmSo.FindProperty("ballSpawnPoint").objectReferenceValue = ballSpawn.transform;
        if (ballPrefabObj != null)
        {
            gmSo.FindProperty("ballPrefab").objectReferenceValue = ballPrefabObj;
        }
        gmSo.FindProperty("mainCamera").objectReferenceValue = camera.GetComponent<Camera>();
        gmSo.ApplyModifiedProperties();
        
        SerializedObject nmSo = new SerializedObject(networkManager.GetComponent<LANNetworkManager>());
        if (playerPrefabObj != null)
        {
            nmSo.FindProperty("playerPrefab").objectReferenceValue = playerPrefabObj;
        }
        nmSo.ApplyModifiedProperties();
        
        // Save scene
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
        
        Debug.Log($"[LANSceneSetup] Created full stadium CricketMatch scene at {scenePath}");
    }

    private static void CreateFielders()
    {
        GameObject fieldersRoot = new GameObject("AIFielders");
        // Classic field positions (angles around the batsman, radius in metres)
        // Ring fielders ~15-20m, deep fielders ~40-55m
        (string name, Vector3 pos)[] positions = new (string, Vector3)[]
        {
            ("Slip",           new Vector3(  2.0f, 0f, -12.0f)),
            ("Point",          new Vector3(-16.0f, 0f, -14.0f)),
            ("Cover",          new Vector3(-20.0f, 0f,  -2.0f)),
            ("MidOff",         new Vector3(-12.0f, 0f,  10.0f)),
            ("MidOn",          new Vector3( 12.0f, 0f,  10.0f)),
            ("Midwicket",      new Vector3( 22.0f, 0f,  -3.0f)),
            ("SquareLeg",      new Vector3( 17.0f, 0f, -15.0f)),
            ("FineLeg",        new Vector3(  8.0f, 0f,  34.0f)),
            ("ThirdMan",       new Vector3(-24.0f, 0f, -30.0f)),
            ("LongOffDeep",    new Vector3(-42.0f, 0f,  20.0f)),
            ("LongOnDeep",     new Vector3( 42.0f, 0f,  20.0f)),
        };

        Material jerseyMat = new Material(Shader.Find("Standard"));
        jerseyMat.color = new Color(0.96f, 0.78f, 0.10f); // Gold jersey (bowling side)

        foreach (var (name, pos) in positions)
        {
            GameObject fielder = new GameObject("Fielder_" + name);
            fielder.transform.SetParent(fieldersRoot.transform);
            fielder.transform.position = pos + Vector3.up * 0.9f;
            FielderAI ai = fielder.AddComponent<FielderAI>();

            // Simple visual: capsule body
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(fielder.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            body.GetComponent<Renderer>().material = jerseyMat;

            // Face the batsman
            fielder.transform.LookAt(new Vector3(0f, pos.y, -9.5f));
        }

        Debug.Log($"[LANSceneSetup] Created {positions.Length} AI fielders");
    }

    private static void CreateSightScreen(Vector3 position)
    {
        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
        screen.name = "SightScreen";
        screen.transform.position = position;
        screen.transform.localScale = new Vector3(10f, 6f, 0.3f);
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = Color.white;
        screen.GetComponent<Renderer>().material = mat;
    }

    private static void CreateStadiumStands()
    {
        GameObject stands = new GameObject("StadiumGrandstands");
        int segments = 48;
        float radius = 72f;
        Material standMat = new Material(Shader.Find("Standard"));
        standMat.color = new Color(0.12f, 0.20f, 0.35f); // Stadium Navy

        Material seatMat = new Material(Shader.Find("Standard"));
        seatMat.color = new Color(0.85f, 0.25f, 0.25f); // Red seating tier

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2;
            Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, 4.5f, Mathf.Sin(angle) * radius);

            GameObject standSection = GameObject.CreatePrimitive(PrimitiveType.Cube);
            standSection.name = $"Stand_{i}";
            standSection.transform.SetParent(stands.transform);
            standSection.transform.position = pos;
            standSection.transform.localScale = new Vector3(10f, 9f, 6f);
            standSection.transform.LookAt(new Vector3(0, 4.5f, 0));
            standSection.GetComponent<Renderer>().material = (i % 2 == 0) ? standMat : seatMat;
        }
    }

    private static void CreateFloodlightTower(Vector3 position)
    {
        GameObject tower = new GameObject("FloodlightTower");
        tower.transform.position = position;

        // Pole
        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "Pole";
        pole.transform.SetParent(tower.transform);
        pole.transform.localPosition = new Vector3(0, 15f, 0);
        pole.transform.localScale = new Vector3(0.8f, 15f, 0.8f);
        Material poleMat = new Material(Shader.Find("Standard"));
        poleMat.color = new Color(0.5f, 0.5f, 0.55f);
        pole.GetComponent<Renderer>().material = poleMat;

        // Light Head
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "LightRack";
        head.transform.SetParent(tower.transform);
        head.transform.localPosition = new Vector3(0, 30f, 0);
        head.transform.localScale = new Vector3(6f, 3f, 1f);
        head.transform.LookAt(Vector3.zero);
        Material headMat = new Material(Shader.Find("Standard"));
        headMat.color = Color.white;
        head.GetComponent<Renderer>().material = headMat;
    }
    
    private static void CreateStumps(Vector3 position)
    {
        GameObject stumps = new GameObject("Stumps");
        stumps.transform.position = position;

        // Try CC0 stump model first
        GameObject stumpModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CricketModels/cricket_stump.fbx");
        Texture2D stumpTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CricketModels/Textures/stump_1.png");
        GameObject bailModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CricketModels/cricket_bail.fbx");
        Texture2D bailTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CricketModels/Textures/bails_1.png");

        if (stumpModel != null)
        {
            // Model may contain one stump; instance it 3 times at regulation spacing
            for (int i = 0; i < 3; i++)
            {
                GameObject stump = (GameObject)PrefabUtility.InstantiatePrefab(stumpModel);
                stump.name = $"Stump_{i}";
                stump.transform.SetParent(stumps.transform);
                stump.transform.localPosition = new Vector3((i - 1) * 0.09f, 0.35f, 0);

                Renderer r = stump.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    float h = r.bounds.size.y;
                    if (h > 0.001f) stump.transform.localScale *= (0.71f / h);
                }
                if (stumpTex != null)
                {
                    foreach (Renderer rend in stump.GetComponentsInChildren<Renderer>())
                    {
                        Material m = new Material(Shader.Find("Standard"));
                        m.mainTexture = stumpTex;
                        rend.material = m;
                    }
                }
                foreach (Collider c in stump.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
        }
        else
        {
            // Three stumps (primitive fallback)
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
        }

        // Bails
        if (bailModel != null)
        {
            for (int i = 0; i < 2; i++)
            {
                GameObject bail = (GameObject)PrefabUtility.InstantiatePrefab(bailModel);
                bail.name = $"Bail_{i}";
                bail.transform.SetParent(stumps.transform);
                bail.transform.localPosition = new Vector3((i - 0.5f) * 0.09f, 0.71f, 0);

                Renderer r = bail.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    float len = r.bounds.size.magnitude;
                    if (len > 0.001f) bail.transform.localScale *= (0.11f / len);
                }
                if (bailTex != null)
                {
                    foreach (Renderer rend in bail.GetComponentsInChildren<Renderer>())
                    {
                        Material m = new Material(Shader.Find("Standard"));
                        m.mainTexture = bailTex;
                        rend.material = m;
                    }
                }
                foreach (Collider c in bail.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
        }
        else
        {
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