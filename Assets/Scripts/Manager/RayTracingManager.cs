using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Mathf;
using UnityEngine.Rendering;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour {

    [Header("View Settings")]
    [SerializeField] bool useShaderInSceneView; // If true, the shader will be applied in the scene view as well
    [SerializeField] bool useRayTracing; // If true, the ray tracing shader will be used
    [SerializeField] bool useImportanceSampling; // If true, the shader will use importance sampling for the rays
    [SerializeField] bool useSimpleShape; // Without raytracing, just getting the shapes of objects
    [SerializeField] Shader rayTracingShader; 
    [SerializeField, Range(0, 32)] int maxBounceCount = 4;
    [SerializeField, Range(0, 128)] int numRaysPerPixel = 2;
    [SerializeField, Range(1e-05f, 179f)] float fieldOfView = 61.2f;

    [Header("Stencil Buffer Info")]
    [SerializeField] int currentLayer = 1;

    [Header("Debug Info")]
    [SerializeField] bool showBounceCount; // If true, the bounce count will be shown (red pixels
    [SerializeField, Range(0, 32)] int bounceThreshold; // If the bounce count for a pixel is greater than this value, the pixel will be red

    [Header("Info")]
    [ReadOnly] [SerializeField] int numMeshInfo;
	[ReadOnly] [SerializeField] int numMeshChunks;
	[ReadOnly] [SerializeField] int numTriangles;
    [ReadOnly] [SerializeField] int numStencils;
    [ReadOnly] [SerializeField] int numSpheres;
    [ReadOnly] [SerializeField] int numRooms;
    [ReadOnly] [SerializeField] int numLights;
	
    [Header("Environment Settings")]
    [SerializeField] bool useEnvironmentLight;
	[SerializeField] Color groundColour;
	[SerializeField] Color skyColourHorizon;
	[SerializeField] Color skyColourZenith;
	[SerializeField] float sunFocus;
	[SerializeField] float sunIntensity;

    [Header("Ambient Lighting")]
    [SerializeField] bool useAmbientLight = false;
    [SerializeField] Color ambientLightColor = Color.white;
    [SerializeField, Range(0, 1)] float ambientLightIntensity = 1.0f;

    // --- Materials ---
    Material rayTracingMaterial;

    // --- Lists ---
    List<Triangle> allTriangles;
	List<MeshInfo> allMeshInfo;

    // --- Buffers ---
	ComputeBuffer sphereBuffer;
    ComputeBuffer triangleBuffer;
	ComputeBuffer meshInfoBuffer;
    ComputeBuffer stencilBuffer;
    ComputeBuffer roomBuffer;
    ComputeBuffer lightInfoBuffer;

    // --- Contants ---
    public const int TriangleLimit = 1500;




    void Start() { }

    void Update() { }

    // Called after each camera (e.g. game or scene camera) has finished rendering into the src texture.
    void OnRenderImage(RenderTexture src, RenderTexture target) {
        bool shouldApplyShader = Camera.current.name != "SceneCamera" || useShaderInSceneView;
        if (shouldApplyShader) {
            InitFrame();
            Graphics.Blit(null, target, rayTracingMaterial);  // Run the ray tracing shader and draw the result to the screen
        }  else {
            Graphics.Blit(src, target); // Copy the source texture to the target texture i.e. do not apply the shader
        }
    }

    void InitFrame(){
		ShaderHelper.InitMaterial(rayTracingShader, ref rayTracingMaterial); // Create materials used in blits
        UpdateCameraParams(Camera.current);
        CreateRooms();
        SetShaderParams();
    }

    // Update the camera parameters in the shader (and change FOV)
    void UpdateCameraParams(Camera cam){
        float planeHeight = cam.nearClipPlane * Tan(cam.fieldOfView * 0.5f * Deg2Rad) * 2;
        float planeWidth = planeHeight * cam.aspect;
        // Send data to shader
        rayTracingMaterial.SetVector("ViewParams", new Vector3(planeWidth, planeHeight, cam.nearClipPlane));
        rayTracingMaterial.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);  
        // Update camera FOV (only if not scene camera)
        if(cam.name != "SceneCamera") cam.fieldOfView = fieldOfView;
    }

    // Loop through all objects (meshes and spheres) and create a list of rooms, then send data to the shader
    void CreateRooms(){
        // -- Get rooms and sort them
        RoomObject[] roomObjects = FindObjectsOfType<RoomObject>(); 
        System.Array.Sort(roomObjects, (a, b) => a.layer.CompareTo(b.layer));
        numRooms = roomObjects.Length;

        // create abstract rooms to send to the shader
        Room[] rooms = new Room[roomObjects.Length];

        // -- Get all the meshes and sort them
        MeshObject[] meshObjects = FindObjectsOfType<MeshObject>();
        System.Array.Sort(meshObjects, (a, b) => a.layer.CompareTo(b.layer));

        // -- Initialize the lists ( if list is null, create a new list, otherwise use the existing one)
		allTriangles ??= new List<Triangle>(); 
		allMeshInfo ??= new List<MeshInfo>();
        allTriangles.Clear();
		allMeshInfo.Clear();

        // -- Get all the spheres
        SphereObject[] sphereObjects = FindObjectsOfType<SphereObject>();
        System.Array.Sort(sphereObjects, (a, b) => a.layer.CompareTo(b.layer));
        Sphere[] spheres = new Sphere[sphereObjects.Length];

        // -- Get all the stencils
        StencilObject[] stencilObjects = FindObjectsOfType<StencilObject>();
        System.Array.Sort(stencilObjects, (a, b) => a.layer.CompareTo(b.layer));
        StencilRect[] stencilRects = new StencilRect[stencilObjects.Length];


        // Light Info
        List<LightInfo> allLightInfos = new List<LightInfo>();
        int numLights = 0; // number of triangles that are ligths (i.e. are emissive)


        // -- Initialize the rooms 
        for(int i = 0; i < numRooms; i++){ 
            rooms[i] = new Room(); 
            roomObjects[i].layer = i+1;
            rooms[i].numSpheres = 0;
            roomObjects[i].numSpheres = 0;
            rooms[i].numMeshes = 0;
            roomObjects[i].numMeshes = 0; 
            rooms[i].numStencils = 0;
            roomObjects[i].numStencils = 0;
        }

        // -- Add meshes to Rooms:
		for (int i = 0; i < meshObjects.Length; i++) {

            int tempLayer = meshObjects[i].layer;

            MeshChunk[] chunks = meshObjects[i].GetSubMeshes();
			foreach (MeshChunk chunk in chunks) {
				RayTracingMaterial material = meshObjects[i].GetMaterial(chunk.subMeshIndex);

                // Check if material is emissive
                bool isEmissive = material.emissionStrength > 0f && material.emissionColor.maxColorComponent > 0f;
                if (isEmissive) {
                    Vector3 sumCenter = Vector3.zero;
                    int triCount = chunk.triangles.Length;

                    foreach (Triangle t in chunk.triangles) {
                        Vector3 center = (t.posA + t.posB + t.posC) / 3f;
                        sumCenter += center;
                    }
                    if (triCount > 0) {
                        Vector3 avgCenter = sumCenter / triCount;
                        allLightInfos.Add(new LightInfo { position = avgCenter });
                        numLights++;
                    }

                    //allLightInfos.Add(new LightInfo { position = chunk.triangles[0].posA + chunk.triangles[0].posB + chunk.triangles[0].posC / 3f });
                    //numLights++;    

                }



				allMeshInfo.Add(new MeshInfo(allTriangles.Count, chunk.triangles.Length, material, chunk.bounds, meshObjects[i].layer));
				allTriangles.AddRange(chunk.triangles);
                rooms[tempLayer-1].numMeshes++;
                roomObjects[tempLayer-1].numMeshes++;
			}   
		}

        // -- Add spheres to Rooms:
        for(int i = 0; i < spheres.Length; i++){
            
            spheres[i] = new Sphere() {
                position = sphereObjects[i].transform.position,
                radius = sphereObjects[i].transform.localScale.x * 0.5f,
                material = sphereObjects[i].material,
                layer = sphereObjects[i].layer,
            };

            int templayer = sphereObjects[i].layer;

            rooms[templayer-1].numSpheres++;
            roomObjects[templayer-1].numSpheres++;
        }

        // -- Add stencils to Rooms:
        for(int i = 0; i < stencilObjects.Length; i++){

            stencilObjects[i].ExtractQuadParameters();
            stencilRects[i] = new StencilRect() {
                center = stencilObjects[i].GetCenter(),
                normal = stencilObjects[i].GetNormal(),
                u = stencilObjects[i].GetU(),
                v= stencilObjects[i].GetV(),
                layer = stencilObjects[i].layer,
                nextLayer = stencilObjects[i].nextLayer,
            };

            int templayer = stencilObjects[i].layer;

            rooms[templayer-1].numStencils++;
            rooms[templayer-1].stencilIndex = i;
            roomObjects[templayer-1].numStencils++;
            roomObjects[templayer-1].stencilIndex = i;
        }

        // -- Add Index to Rooms:
        rooms[0].meshIndex = 0;
        rooms[0].spheresIndex = 0;
        rooms[0].stencilIndex = 0;
        roomObjects[0].meshIndex = 0;
        roomObjects[0].spheresIndex = 0;
        roomObjects[0].stencilIndex = 0;
        //Debug.Log("rooms[0].meshIndex: " + rooms[0].meshIndex);

        for(int i = 1; i < numRooms; i++){
            rooms[i].meshIndex = rooms[i-1].numMeshes + rooms[i-1].meshIndex;
            //Debug.Log("rooms[i].meshIndex: " + rooms[i].meshIndex);
            rooms[i].spheresIndex = rooms[i-1].numSpheres + rooms[i-1].spheresIndex;
            rooms[i].stencilIndex = rooms[i-1].numStencils + rooms[i-1].stencilIndex;
            roomObjects[i].meshIndex = rooms[i].meshIndex;
            roomObjects[i].spheresIndex = rooms[i].spheresIndex;
            roomObjects[i].stencilIndex = rooms[i].stencilIndex;
        }



        // -- SEND DATA TO SHADER -- 
        
        // Send mesh data to the shader
        ShaderHelper.CreateStructuredBuffer(ref triangleBuffer, allTriangles);
		ShaderHelper.CreateStructuredBuffer(ref meshInfoBuffer, allMeshInfo);
		rayTracingMaterial.SetBuffer("Triangles", triangleBuffer);
		rayTracingMaterial.SetBuffer("AllMeshInfo", meshInfoBuffer);
		rayTracingMaterial.SetInt("NumMeshes", allMeshInfo.Count);

        // Send Sphere data to the shader
        ShaderHelper.CreateStructuredBuffer(ref sphereBuffer, spheres);
        rayTracingMaterial.SetBuffer("Spheres", sphereBuffer);
        rayTracingMaterial.SetInt("NumSpheres", sphereObjects.Length);

        // Send stencil data to the shader
        ShaderHelper.CreateStructuredBuffer(ref stencilBuffer, stencilRects);
        rayTracingMaterial.SetBuffer("StencilRects", stencilBuffer);
        rayTracingMaterial.SetInt("NumStencilRects", stencilObjects.Length);

        // Send room data to the shader
        ShaderHelper.CreateStructuredBuffer(ref roomBuffer, rooms);
        rayTracingMaterial.SetBuffer("Rooms", roomBuffer);
        rayTracingMaterial.SetInt("NumRooms", numRooms);

        // Send light data to the shader
        ShaderHelper.CreateStructuredBuffer(ref lightInfoBuffer, allLightInfos);
        rayTracingMaterial.SetBuffer("LightInfos", lightInfoBuffer);
        rayTracingMaterial.SetInt("NumLights", numLights);
       
        // ----------------------------

    } 

    void SetShaderParams() {
		rayTracingMaterial.SetInt("MaxBounceCount", maxBounceCount);
		rayTracingMaterial.SetInt("NumRaysPerPixel", numRaysPerPixel);
        rayTracingMaterial.SetInt("UseSimpleShape", useSimpleShape ? 1 : 0);
        rayTracingMaterial.SetInt("cameraLayer", currentLayer);

        // View options
        rayTracingMaterial.SetInt("useRayTracing", useRayTracing ? 1 : 0);
        rayTracingMaterial.SetInt("useImportanceSampling", useImportanceSampling ? 1 : 0);

        // Debug Info
        rayTracingMaterial.SetInt("ShowBounceCount", showBounceCount ? 1 : 0);
        rayTracingMaterial.SetInt("bounceThreshold", bounceThreshold);

        // Environment Light 
        rayTracingMaterial.SetInteger("useEnvironmentLight", useEnvironmentLight ? 1 : 0);
		rayTracingMaterial.SetColor("GroundColour", groundColour);
		rayTracingMaterial.SetColor("SkyColourHorizon", skyColourHorizon);
		rayTracingMaterial.SetColor("SkyColourZenith", skyColourZenith);
		rayTracingMaterial.SetFloat("SunFocus", sunFocus);
		rayTracingMaterial.SetFloat("SunIntensity", sunIntensity);
        // Ambient Light 
        rayTracingMaterial.SetInt("useAmbientLight", useAmbientLight ? 1 : 0);
        rayTracingMaterial.SetColor("AmbientLightColor", ambientLightColor);
        rayTracingMaterial.SetFloat("AmbientLightIntensity", ambientLightIntensity);
	}


    // Called when the script instance is being loaded
    void OnDisable() {
        ShaderHelper.Release(triangleBuffer, meshInfoBuffer);
        ShaderHelper.Release(sphereBuffer);
        ShaderHelper.Release(stencilBuffer);
        ShaderHelper.Release(roomBuffer);
        ShaderHelper.Release(lightInfoBuffer);
    }

    void OnDestroy() {
    }

    // Called when the script is loaded or a value is changed in the inspector (Called in the editor only)
    void OnValidate(){
		maxBounceCount = Mathf.Max(0, maxBounceCount);
		numRaysPerPixel = Mathf.Max(1, numRaysPerPixel);
		sunFocus = Mathf.Max(1, sunFocus);
		sunIntensity = Mathf.Max(0, sunIntensity);
	}


}
