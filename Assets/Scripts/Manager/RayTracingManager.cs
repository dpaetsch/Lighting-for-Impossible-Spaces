using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Mathf;


[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    public const int TriangleLimit = 1500;

    [SerializeField] bool useShaderInSceneView;

    [SerializeField] bool useSimpleShape; // Without raytracing, just getting the shapes of objects

    [SerializeField] Shader rayTracingShader;

    [SerializeField, Range(0, 32)] int maxBounceCount = 4;
    [SerializeField, Range(0, 128)] int numRaysPerPixel = 2;


    [Header("Info")]
    
    [ReadOnly] [SerializeField] int numMeshInfo;
	[ReadOnly] [SerializeField] int numMeshChunks;
	[ReadOnly] [SerializeField] int numTriangles;
    [ReadOnly] [SerializeField] int numStencils;
    [ReadOnly] [SerializeField] int numSpheres;
    [ReadOnly] [SerializeField] int numRooms;

    List<Triangle> allTriangles;
	List<MeshInfo> allMeshInfo;
	

    [Header("Environment Settings")]

    [SerializeField] EnvironmentSettings environmentSettings;

    [Header("Ambient Lighting")]
    [SerializeField] bool useAmbientLight = false;
    [SerializeField] Color ambientLightColor = Color.white;
    [SerializeField, Range(0, 1)] float ambientLightIntensity = 1.0f;




    [Header("Stencil Buffer Info")]
    [SerializeField] int currentLayer = 1;


    Material rayTracingMaterial;


    // --- Buffers ---
	ComputeBuffer sphereBuffer;
    ComputeBuffer triangleBuffer;
	ComputeBuffer meshInfoBuffer;
    ComputeBuffer stencilBuffer;
    ComputeBuffer roomBuffer;




    // Called after each camera (e.g. game or scene camera) has finished rendering into the src texture.
    void OnRenderImage(RenderTexture src, RenderTexture target){
        if(Camera.current.name != "SceneCamera" || useShaderInSceneView){
            InitFrame();
            // Set up a material using the ray tracing shader
            ShaderHelper.InitMaterial(rayTracingShader, ref rayTracingMaterial);
            UpdateCameraParams(Camera.current);

            // Run the ray tracing shader and draw the result to the screen
            Graphics.Blit(null, target, rayTracingMaterial);
        } else {
            InitFrame();
            Graphics.Blit(src, target);
        } 
    }

    void UpdateCameraParams(Camera cam){
        float planeHeight = cam.nearClipPlane * Tan(cam.fieldOfView * 0.5f * Deg2Rad) * 2;
        float planeWidth = planeHeight * cam.aspect;
        // Send data to shader
        rayTracingMaterial.SetVector("ViewParams", new Vector3(planeWidth, planeHeight, cam.nearClipPlane));
        rayTracingMaterial.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);    
    }

    void InitFrame(){
        // Create materials used in blits
		ShaderHelper.InitMaterial(rayTracingShader, ref rayTracingMaterial);

        // Update Data
        UpdateCameraParams(Camera.current);
        SetShaderParams();
        CreateRooms();
    }


    // Loop through all objects (meshes and spheres) and create a list of rooms
    void CreateRooms(){

        // -- Get rooms and sort them
        RoomObject[] roomObjects = FindObjectsOfType<RoomObject>(); 
        System.Array.Sort(roomObjects, (a, b) => a.layer.CompareTo(b.layer));
        numRooms = roomObjects.Length;

        // create abstract rooms to send to the shader
        Room[] rooms = new Room[roomObjects.Length];

        // -- Get all the meshes
        MeshObject[] meshObjects = FindObjectsOfType<MeshObject>();
        System.Array.Sort(meshObjects, (a, b) => a.layer.CompareTo(b.layer));
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

            int templayer = meshObjects[i].layer;

            MeshChunk[] chunks = meshObjects[i].GetSubMeshes();
			foreach (MeshChunk chunk in chunks) {
				RayTracingMaterial material = meshObjects[i].GetMaterial(chunk.subMeshIndex);
				allMeshInfo.Add(new MeshInfo(allTriangles.Count, chunk.triangles.Length, material, chunk.bounds, meshObjects[i].layer));
				allTriangles.AddRange(chunk.triangles);
                rooms[templayer-1].numMeshes++;
                roomObjects[templayer-1].numMeshes++;
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
       
        // ----------------------------

    } 







        

    void SetShaderParams() {
		rayTracingMaterial.SetInt("MaxBounceCount", maxBounceCount);
		rayTracingMaterial.SetInt("NumRaysPerPixel", numRaysPerPixel);

        rayTracingMaterial.SetInt("UseSimpleShape", useSimpleShape ? 1 : 0);

        rayTracingMaterial.SetInteger("EnvironmentEnabled", environmentSettings.enabled ? 1 : 0);
		rayTracingMaterial.SetColor("GroundColour", environmentSettings.groundColour);
		rayTracingMaterial.SetColor("SkyColourHorizon", environmentSettings.skyColourHorizon);
		rayTracingMaterial.SetColor("SkyColourZenith", environmentSettings.skyColourZenith);
		rayTracingMaterial.SetFloat("SunFocus", environmentSettings.sunFocus);
		rayTracingMaterial.SetFloat("SunIntensity", environmentSettings.sunIntensity);

        rayTracingMaterial.SetInt("cameraLayer", currentLayer);

        rayTracingMaterial.SetInt("UseAmbientLight", useAmbientLight ? 1 : 0);
        rayTracingMaterial.SetColor("AmbientLightColor", ambientLightColor);
        rayTracingMaterial.SetFloat("AmbientLightIntensity", ambientLightIntensity);
	}



    // Called when the script instance is being loaded
    void OnDisable() {
        ShaderHelper.Release(triangleBuffer, meshInfoBuffer);
        ShaderHelper.Release(sphereBuffer);
        ShaderHelper.Release(stencilBuffer);
        ShaderHelper.Release(roomBuffer);
	}

    // Called when the script is loaded or a value is changed in the inspector (Called in the editor only)
    void OnValidate(){
		maxBounceCount = Mathf.Max(0, maxBounceCount);
		numRaysPerPixel = Mathf.Max(1, numRaysPerPixel);
		environmentSettings.sunFocus = Mathf.Max(1, environmentSettings.sunFocus);
		environmentSettings.sunIntensity = Mathf.Max(0, environmentSettings.sunIntensity);

	}




}
