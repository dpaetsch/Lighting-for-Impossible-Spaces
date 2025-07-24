using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Mathf;
using UnityEngine.Rendering;
using System.Diagnostics;
using System.Linq; // for Min() function in dictionaries

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour {

    [Header("View Settings")]
    [SerializeField] bool useShaderInSceneView; // If true, the shader will be applied in the scene view as well
    [SerializeField] bool useRayTracing; // If true, the ray tracing shader will be used
    [SerializeField] bool useImportanceSampling; // If true, the shader will use importance sampling for the rays
    [SerializeField] bool useSimpleShape; // Without raytracing, just getting the shapes of objects
    [SerializeField] bool staticObjects; // If true, the objects will not be updated every frame, but only when they are changed in the editor

    [SerializeField] Shader rayTracingShader; 
    [SerializeField, Range(0, 32)] int maxBounceCount = 4;
    [SerializeField, Range(0, 128)] int numRaysPerPixel = 2;
    [SerializeField, Range(1e-05f, 179f)] float fieldOfView = 61.2f;
    [SerializeField] int maxPropagationDepth = 3;

    [Header("Bounding Volume Hierarchy")]
    [SerializeField] bool useBVH; // If true, the shader will use a BVH for the triangles
    [SerializeField] bool useFullObjectsInBVH; // If true, the BVH will use the full objects (for meshes) instead of just triangles 
    [SerializeField] bool skipConstruction = true; // If true, the BVH will use colors for each depth level
    [SerializeField] bool showBVHDepth;  // if true, it will show the bounding box of the depth of the BVH
    [SerializeField] bool accumulateBVHColors; // If true, the BVH will accumulate colors for each depth level
    [SerializeField, Range(0, 16)] int bvhDepth = 1; // Depth of the BVH tree, 0 means no BVH
    [SerializeField] int bvhMaxDepth = 16; // Scale of the bounding box
    [ReadOnly] [SerializeField] int bvhMaxDepthReached;
    [ReadOnly] [SerializeField] int numBVHNodes;
    [ReadOnly] [SerializeField] int numWrapperObjects; 

    [Header("Stencil Buffer Info")]
    [SerializeField, Range(0,5)] public int currentLayer = 0; 
    [ReadOnly] [SerializeField] public int virtualizedCurrentLayer = 0;
    [SerializeField, Range(0,5)] public int nextLayer = 1; // Layer that is also activated (for player movement)
    [SerializeField] public bool singleLayer; // Forces nexLayer = currentLayer
    [SerializeField] public bool connected; // Forces nextLayer = currentLayer + 1 

    [Header("Debug Info")]
    [SerializeField] bool showIntersectionCount; // If true, the bounce count will be shown (red pixels
    [SerializeField, Range(0, 500)] int maxIntersectionTests = 500; // Maximum number of intersection tests per ray
    [SerializeField] bool showFPSCounter; // If true, the FPS counter will be shown in the scene view
    [SerializeField] FPSCounter fpsCounter; // FPS counter object

    [Header("Object Info")]
    [ReadOnly] [SerializeField] int numRooms;
    [ReadOnly] [SerializeField] int numMeshes;
	[ReadOnly] [SerializeField] int numTriangles; 
    [ReadOnly] [SerializeField] int numSpheres;
    [ReadOnly] [SerializeField] int numStencils; 
    [ReadOnly] [SerializeField] int numLights; // number of meshes that are emissive
	
    [Header("Environment Settings")]
    // Environment light is a light that is determined from the directional light in the scene
    [SerializeField] bool useEnvironmentLight;
	[SerializeField] Color groundColour;
	[SerializeField] Color skyColourHorizon;
	[SerializeField] Color skyColourZenith;
	[SerializeField] float sunFocus;
	[SerializeField] float sunIntensity;

    [Header("Ambient Lighting")]
    // Ambient light is a constant light that is applied to the scene
    [SerializeField] bool useAmbientLight = false; 
    [SerializeField] Color ambientLightColor = Color.white;
    [SerializeField, Range(0, 1)] float ambientLightIntensity = 1.0f;


    // View Parameters
    Vector3 viewParams;
    Matrix4x4 CamLocalToWorldMatrix;

    // --- Materials ---
    Material rayTracingMaterial;  

    // --- Active Layer Info ---
    Dictionary<int, int> activeLayers; // Dictionary to keep track of active layers and their indices in the roomObjects array

    // --- Object Lists --- 
    // These object lists represent "real" objects in the scene. 
    // Room, Mesh, Sphere, Stencil are the only ones that you manually apply to GameObjects in the scene.
    // Triangles and Lights are automatically created from the objects based on their properties.
    RoomObject[] roomObjects;
    List<MeshObject> meshObjects;
    List<TriangleObject> triangleObjects; // List of triangle objects in this room
    List<SphereObject> sphereObjects;
    List<StencilObject> stencilObjects; 
    List<LightObject> lightObjects; // List of light objects in the scene

    // --- Info Lists to send to Shader ---
    // These Info Lists represent curated data that is sent to the shader for rendering. infos only contain the necessary data for rendering, not the full objects.
    RoomInfo[] roomInfos;
    MeshInfo[] meshInfos;
    TriangleInfo[] triangleInfos;
    SphereInfo[] sphereInfos;
    StencilInfo[] stencilInfos;
    LightInfo[] lightInfos;

    // --- BVH lists ---
    List<WrapperObject> wrapperObjects; // List of wrapper objects for triangles and spheres
    List<BVHNode> bvhNodes; // List of BVH nodes in the hierarchy
    WrapperInfo[] wrapperInfos;
    BVHNodeInfo[] bvhNodeInfos;

    // --- Buffers ---
	ComputeBuffer sphereInfoBuffer;
    ComputeBuffer triangleInfoBuffer;
	ComputeBuffer meshInfoBuffer;
    ComputeBuffer stencilInfoBuffer;
    ComputeBuffer roomInfoBuffer;
    ComputeBuffer lightInfoBuffer;
    ComputeBuffer bvhNodeInfoBuffer; 
    ComputeBuffer wrapperInfoBuffer;



    // Called after each camera (e.g. game or scene camera) has finished rendering into the src texture.
    void OnRenderImage(RenderTexture src, RenderTexture target) {
        bool activeMode = useRayTracing || useSimpleShape || useImportanceSampling; // If there is an active viewing mode 
        bool shouldApplyShader = Camera.current.name != "SceneCamera" || useShaderInSceneView; // If it is not the scene camera or if the shader should be applied in the scene view

        if (shouldApplyShader && activeMode) {
            InitFrame();
            Graphics.Blit(null, target, rayTracingMaterial);  // Run the ray tracing shader and draw the result to the screen
        }  else {
            Graphics.Blit(src, target); // Copy the source texture to the target texture i.e. do not apply the shader
        }
    }

    void InitFrame(){
		ShaderHelper.InitMaterial(rayTracingShader, ref rayTracingMaterial); // Create materials used in blits
        UpdateCameraParams(Camera.current);

        SanityChecks();

        CreateLocalObjectlists(); // Create the local object lists for each room and process the objects in the rooms
        CreateGlobalObjectLists(); // Create the global object lists and assign the correct global indices to the objects
        CreateBVHs(); // Create the BVH for the objects in the scene
        AssignGlobalInfoLists(); // Assign the info lists to the arrays
        
        SendBuffersToShader(); // Sends the buffers to the shader
        SendParametersToShader(); // Sends the parameters to the shader
    }

    // Update the camera parameters in the shader (and change FOV)
    void UpdateCameraParams(Camera cam){
        float planeHeight = cam.nearClipPlane * Tan(cam.fieldOfView * 0.5f * Deg2Rad) * 2;
        float planeWidth = planeHeight * cam.aspect;

        viewParams = new Vector3(planeWidth, planeHeight, cam.nearClipPlane);
        CamLocalToWorldMatrix = cam.transform.localToWorldMatrix;

        // Update camera FOV (only if not scene camera)
        if(cam.name != "SceneCamera") cam.fieldOfView = fieldOfView;
    }


    // For each room, create the local object lists and process the objects.
    void CreateLocalObjectlists(){

        //roomObjects = FindObjectsOfType<RoomObject>();
        roomObjects = FindObjectsOfType<RoomObject>();
        numRooms = roomObjects.Length;

        // Sort rooms in ascending order by layer
        System.Array.Sort(roomObjects, (a, b) => a.layer.CompareTo(b.layer));

        // --- Process all Objects in the Rooms ---
        // This creates for each rooom:
        // - local Mesh objects list
        // - local Triangle objects list
        // - local Sphere objects list
        // - local Stencil objects list
        // - local Light objects list
        // Local means that the indices are only valid within the room, not globally. 
        foreach(RoomObject roomObject in roomObjects){
            roomObject.processObjects(); // Process the objects in the room
        }

        // See which layers actually exist (sort or virtualization)
        // This is so that we can then see which room as which index (in case some room are deactivated, so it doesn't crash)
        activeLayers = new Dictionary<int, int>();
        // Note: room list should be ordered by layer number (increasing)
        for (int i = 0; i < numRooms; i++) {
            int layer = roomObjects[i].layer;
            if (!activeLayers.ContainsKey(layer)) {
                activeLayers[layer] = i; // Store the index of the first room with this layer
            }
        }

        // We need to check that if there is a stencil buffer in a room, that the connecting room (the next layer of the stencil buffer), actually exists in the scene.
        // If it doesn't exist, then we remove it from the local lists
        foreach (RoomObject roomObject in roomObjects) {
            // Collect stencil objects to remove
            List<StencilObject> toRemove = new List<StencilObject>();
            foreach (StencilObject stencilObject in roomObject.stencilObjects) {
                if (!activeLayers.ContainsKey(stencilObject.nextLayer)) {
                    toRemove.Add(stencilObject);
                }
            }
            // Remove stencils 
            foreach (StencilObject stencil in toRemove) {
                roomObject.stencilObjects.Remove(stencil);
                roomObject.numStencils--;
            }
        }

    }

    // After processing all the objects in the rooms, we create global object lists 
    // and assign the correct global indices to the objects.
    void CreateGlobalObjectLists(){

        // Reset the global object lists
        meshObjects = new List<MeshObject>();
        triangleObjects = new List<TriangleObject>();
        sphereObjects = new List<SphereObject>();
        stencilObjects = new List<StencilObject>();
        lightObjects = new List<LightObject>();

        numMeshes = 0;
        numTriangles = 0;
        numSpheres = 0;
        numStencils = 0;
        numLights = 0;

        // These indices are used to keep track of the global indices of the objects in the global lists
        int globalMeshesIndex = 0; // Index of the mesh in the global list
        int globalTrianglesIndex = 0; // Start index for triangles in the global list 
        int globalSpheresIndex = 0; // Start index for spheres in the global list
        int globalStencilsIndex = 0; // Start index for stencils in the global list 
        // We don't need to keep track of lights indexes, since we always search for all lights

        // We go through each room, and all of the object lists in the room. 
        // Then we assign for each object in the room, the global indices
        // We also assign the correct virtualized layer (in case there is a missing room, all the layer indices will shift down to avoid having gaps in the array and causing a crash)
        for(int i = 0; i < numRooms; i++){

            RoomObject roomObject = roomObjects[i];

            int virtualizedLayerOfRoom = activeLayers[roomObject.layer];
            roomObject.virtualizedLayer = virtualizedLayerOfRoom;
            
            // Set global indices room offsets
            roomObject.globalMeshesIndex = globalMeshesIndex; // Set the global mesh index for the room
            roomObject.globalTrianglesIndex = globalTrianglesIndex; // Set the global triangles index for the room
            roomObject.globalSpheresIndex = globalSpheresIndex; // Set the global spheres index for the room
            roomObject.globalStencilsIndex = globalStencilsIndex; // Set the global stencil index for the room

            int tempTriangleOffset = 0;

            // Assign global indices to the objects in the room, and als virtualized layers
            for(int j = 0; j < roomObject.numMeshes; j++){
                MeshObject meshObject = roomObject.meshObjects[j];
                meshObject.virtualizedLayer = virtualizedLayerOfRoom;
                meshObject.globalTrianglesStartIndex = roomObject.globalTrianglesIndex + tempTriangleOffset; 
                tempTriangleOffset += meshObject.triangleCount; // offset for the next mesh
            }

            for(int j = 0; j < roomObject.numTriangles; j++){
                TriangleObject triangleObject = roomObject.triangleObjects[j];
                triangleObject.globalMeshesIndex = roomObject.globalMeshesIndex + triangleObject.localMeshesIndex;
                // Add room offset, then mesh object offset, then the specific offset of the triangle.
            }

            for(int j = 0; j < roomObject.numSpheres; j++){
                SphereObject sphereObject = roomObject.sphereObjects[j];
                sphereObject.virtualizedLayer = virtualizedLayerOfRoom;
            }

            for(int j = 0; j < roomObject.numStencils; j++){
                StencilObject stencilObject = roomObject.stencilObjects[j];
                stencilObject.virtualizedLayer = virtualizedLayerOfRoom;
                stencilObject.virtualizedNextLayer = activeLayers[stencilObject.nextLayer];     
            }

            for(int j = 0; j < roomObject.numLights; j++){
                LightObject lightObject = roomObject.lightObjects[j];
                lightObject.virtualizedLayer = virtualizedLayerOfRoom;
            }


             // Add local lists to global lists
            meshObjects.AddRange(roomObject.meshObjects);
            triangleObjects.AddRange(roomObject.triangleObjects);
            sphereObjects.AddRange(roomObject.sphereObjects);
            stencilObjects.AddRange(roomObject.stencilObjects);
            lightObjects.AddRange(roomObject.lightObjects);

            // We add the number of objects from the previous room to the list, then the next index is the start of the next room.
            globalMeshesIndex += roomObject.numMeshes;
            globalTrianglesIndex += roomObject.numTriangles; 
            globalSpheresIndex += roomObject.numSpheres;
            globalStencilsIndex += roomObject.numStencils;

            // Update total numbers
            numMeshes += roomObject.numMeshes; 
            numTriangles += roomObject.numTriangles; 
            numSpheres += roomObject.numSpheres;
            numStencils += roomObject.numStencils; 
            numLights += roomObject.numLights;
        }

    }



    void CreateBVHs() {
        // BVH Construciton for each room
        wrapperObjects = new List<WrapperObject>();
        bvhNodes = new List<BVHNode>();

        numBVHNodes = 0;
        numWrapperObjects = 0;
        bvhMaxDepthReached = 0;

        if(skipConstruction) {
            return; // If BVH is not enabled, skip the construction
        }

        // Create a BVH tree for every room
        for(int r = 0; r < numRooms; r++){
            BVH bvh = new BVH(meshObjects, roomObjects[r].globalMeshesIndex, roomObjects[r].numMeshes, sphereObjects, roomObjects[r].globalSpheresIndex, roomObjects[r].numSpheres, bvhMaxDepth, roomObjects[r].virtualizedLayer, useFullObjectsInBVH);

            roomObjects[r].bvhNodesIndex = bvhNodes.Count; // Index of the first BVH node in the list
            roomObjects[r].wrappersIndex = wrapperObjects.Count; // Index of the first wrapper in the list
            //Debug.Log("Room " + r + " has node index: " + roomObjects[r].bvhNodesIndex + " and wrappers index: " + roomObjects[r].wrappersIndex + " with " + bvh.numWrapperObjects + " wrappers and " + bvh.numBVHNodes + " BVH nodes.");

            roomObjects[r].numbvhNodes = bvh.numBVHNodes; // Number of BVH nodes in the room
            roomObjects[r].numWrappers = bvh.numWrapperObjects; // Number of wrapper objects in the room
        
            numBVHNodes += bvh.numBVHNodes;
            numWrapperObjects += bvh.numWrapperObjects;
            bvhMaxDepthReached = Max(bvhMaxDepthReached, bvh.maxDepthReached);

            List<BVHNode> bvhNodesRoom = bvh.GetBVHNodes();
            List<WrapperObject> wrapperObjectsRoom = bvh.GetWrapperObjects();

            wrapperObjects.AddRange(wrapperObjectsRoom);
            bvhNodes.AddRange(bvhNodesRoom);
        }

    } 



    void AssignGlobalInfoLists(){
        roomInfos = new RoomInfo[numRooms];
        meshInfos = new MeshInfo[numMeshes];
        triangleInfos = new TriangleInfo[numTriangles];
        sphereInfos = new SphereInfo[numSpheres];
        stencilInfos = new StencilInfo[numStencils];
        lightInfos = new LightInfo[numLights];
       
        wrapperInfos = new WrapperInfo[numWrapperObjects];
        bvhNodeInfos = new BVHNodeInfo[numBVHNodes];

        for(int i = 0; i < numRooms; i++){
            roomInfos[i] = new RoomInfo();
            
            //roomInfos[i].layer = roomObjects[i].layer;
            roomInfos[i].layer = roomObjects[i].virtualizedLayer;

            roomInfos[i].globalMeshesIndex = roomObjects[i].globalMeshesIndex;
            roomInfos[i].numMeshes = roomObjects[i].numMeshes;
            roomInfos[i].globalSpheresIndex = roomObjects[i].globalSpheresIndex;
            roomInfos[i].numSpheres = roomObjects[i].numSpheres;
            roomInfos[i].globalStencilsIndex = roomObjects[i].globalStencilsIndex;
            roomInfos[i].numStencils = roomObjects[i].numStencils;

            roomInfos[i].numWrappers = roomObjects[i].numWrappers;
            roomInfos[i].wrappersIndex = roomObjects[i].wrappersIndex;
            roomInfos[i].numbvhNodes = roomObjects[i].numbvhNodes;
            roomInfos[i].bvhNodesIndex = roomObjects[i].bvhNodesIndex;
        }   

        for(int i = 0; i < numMeshes; i++){
            meshInfos[i] = new MeshInfo();
            meshInfos[i].globalTrianglesStartIndex = meshObjects[i].globalTrianglesStartIndex;
            meshInfos[i].triangleCount = meshObjects[i].triangleCount;
            meshInfos[i].material = meshObjects[i].material;
            meshInfos[i].boundsMin = meshObjects[i].boundsMin;
            meshInfos[i].boundsMax = meshObjects[i].boundsMax;
            // meshInfos[i].layer = meshObjects[i].layer;
            meshInfos[i].layer = meshObjects[i].virtualizedLayer;
        }

        for(int i = 0; i < numTriangles; i++){
            triangleInfos[i] = new TriangleInfo();
            triangleInfos[i].v0 = triangleObjects[i].v0;
            triangleInfos[i].v1 = triangleObjects[i].v1;
            triangleInfos[i].v2 = triangleObjects[i].v2;
            triangleInfos[i].n0 = triangleObjects[i].n0;
            triangleInfos[i].n1 = triangleObjects[i].n1;
            triangleInfos[i].n2 = triangleObjects[i].n2;
            triangleInfos[i].globalMeshesIndex = triangleObjects[i].globalMeshesIndex; // Mesh index of the triangle in the global list
        }

        for(int i = 0; i < numSpheres; i++){
            sphereInfos[i] = new SphereInfo();
            sphereInfos[i].position = sphereObjects[i].transform.position;
            sphereInfos[i].radius = sphereObjects[i].transform.localScale.x * 0.5f;
            sphereInfos[i].material = sphereObjects[i].material;
            // sphereInfos[i].layer = sphereObjects[i].layer;
            sphereInfos[i].layer = sphereObjects[i].virtualizedLayer;
        }

        for(int i = 0; i < numStencils; i++){
            stencilInfos[i] = new StencilInfo();
            stencilInfos[i].center = stencilObjects[i].GetCenter();
            stencilInfos[i].normal = stencilObjects[i].GetNormal();
            stencilInfos[i].u = stencilObjects[i].GetU();
            stencilInfos[i].v= stencilObjects[i].GetV();
            // stencilInfos[i].layer = stencilObjects[i].layer;
            stencilInfos[i].layer = stencilObjects[i].virtualizedLayer;
            // stencilInfos[i].nextLayer = stencilObjects[i].nextLayer;
            stencilInfos[i].nextLayer = stencilObjects[i].virtualizedNextLayer;
        }

        for(int i = 0; i < numLights; i++){
            lightInfos[i] = new LightInfo();
            lightInfos[i].position = lightObjects[i].position;
            lightInfos[i].radius = lightObjects[i].radius;
            // lightInfos[i].layer = lightObjects[i].layer;
            lightInfos[i].layer = lightObjects[i].virtualizedLayer;
        }

        // These have to be global indices for the lists, therefore we need to offset the indices by the room index
        for(int i = 0; i < numBVHNodes; i++){
            bvhNodeInfos[i] = new BVHNodeInfo {
                minBounds = bvhNodes[i].minBounds,
                maxBounds = bvhNodes[i].maxBounds,
                isLeaf = bvhNodes[i].isLeaf ? 1 : 0,
                startWrapperIndex = bvhNodes[i].startWrapperIndex + roomObjects[bvhNodes[i].layer].wrappersIndex, // Offset by the room index
                lengthOfWrappers = bvhNodes[i].lengthOfWrappers,
                leftChildIndex = bvhNodes[i].isLeaf? -1 :  bvhNodes[i].leftChildIndex + roomObjects[bvhNodes[i].layer].bvhNodesIndex, // Offset by the room index
                rightChildIndex = bvhNodes[i].isLeaf? -1 : bvhNodes[i].rightChildIndex + roomObjects[bvhNodes[i].layer].bvhNodesIndex // Offset by the room index
            };
        }


        
        for(int i = 0; i < numWrapperObjects; i++){
            wrapperInfos[i] = new WrapperInfo {
                minBounds = wrapperObjects[i].minBounds,
                maxBounds = wrapperObjects[i].maxBounds,
                isTriangle = wrapperObjects[i].isTriangle ? 1 : 0,
                meshIndex = wrapperObjects[i].isTriangle ? wrapperObjects[i].meshIndex + roomObjects[wrapperObjects[i].layer].globalMeshesIndex : -1, // mesh index in wrapper + Offset by the room mesh index ( or -1 if it's a sphere)
                index =  wrapperObjects[i].index + (wrapperObjects[i].isTriangle ? roomObjects[wrapperObjects[i].layer].globalTrianglesIndex :  roomObjects[wrapperObjects[i].layer].globalSpheresIndex) // index of triangle or sphere in wrapper + Offset by the room index               
            };
        }

    }



    void SendBuffersToShader() {
        // Create the buffers for the shader
        ShaderHelper.CreateStructuredBuffer(ref meshInfoBuffer, meshInfos);
        ShaderHelper.CreateStructuredBuffer(ref triangleInfoBuffer, triangleInfos);
        ShaderHelper.CreateStructuredBuffer(ref sphereInfoBuffer, sphereInfos);
        ShaderHelper.CreateStructuredBuffer(ref stencilInfoBuffer, stencilInfos);
        ShaderHelper.CreateStructuredBuffer(ref roomInfoBuffer, roomInfos);
        ShaderHelper.CreateStructuredBuffer(ref lightInfoBuffer, lightInfos);
        ShaderHelper.CreateStructuredBuffer(ref bvhNodeInfoBuffer, bvhNodeInfos);
        ShaderHelper.CreateStructuredBuffer(ref wrapperInfoBuffer, wrapperInfos);

        // Send the buffers to the shader
        rayTracingMaterial.SetBuffer("MeshInfos", meshInfoBuffer);
        rayTracingMaterial.SetBuffer("TriangleInfos", triangleInfoBuffer);
        rayTracingMaterial.SetBuffer("SphereInfos", sphereInfoBuffer);
        rayTracingMaterial.SetBuffer("StencilInfos", stencilInfoBuffer);
        rayTracingMaterial.SetBuffer("RoomInfos", roomInfoBuffer);
        rayTracingMaterial.SetBuffer("LightInfos", lightInfoBuffer);
        rayTracingMaterial.SetBuffer("BvhNodeInfos", bvhNodeInfoBuffer);
        rayTracingMaterial.SetBuffer("WrapperInfos", wrapperInfoBuffer);


        // Send the number of objects to the shader
        rayTracingMaterial.SetInt("NumMeshes", numMeshes);
        rayTracingMaterial.SetInt("NumTriangles", numTriangles);
        rayTracingMaterial.SetInt("NumSpheres", numSpheres);
        rayTracingMaterial.SetInt("NumStencils", numStencils);
        rayTracingMaterial.SetInt("NumRooms", numRooms);
        rayTracingMaterial.SetInt("NumLights", numLights);
        rayTracingMaterial.SetInt("NumBVHNodes", numBVHNodes);
        rayTracingMaterial.SetInt("NumWrappers", numWrapperObjects);
    }


    void SendParametersToShader() {
        rayTracingMaterial.SetInt("cameraLayer", currentLayer);
        rayTracingMaterial.SetInt("nextLayer", nextLayer);

        // Ray Tracing Settings
		rayTracingMaterial.SetInt("NumRaysPerPixel", numRaysPerPixel);
        rayTracingMaterial.SetInt("MaxBounceCount", maxBounceCount);
        rayTracingMaterial.SetInt("maxPropagationDepth", maxPropagationDepth);

        // View options
        rayTracingMaterial.SetInt("useRayTracing", useRayTracing ? 1 : 0);
        rayTracingMaterial.SetInt("useImportanceSampling", useImportanceSampling ? 1 : 0);
        rayTracingMaterial.SetInt("UseSimpleShape", useSimpleShape ? 1 : 0);

        // BVH
        rayTracingMaterial.SetInt("useBVH", useBVH ? 1 : 0);
        rayTracingMaterial.SetInt("useFullObjectsInBVH", useFullObjectsInBVH ? 1 : 0);
        rayTracingMaterial.SetInt("showBVHDepth", showBVHDepth ? 1 : 0);
        rayTracingMaterial.SetInt("bvhDepth", bvhDepth);
        rayTracingMaterial.SetInt("bvhMaxDepth", bvhMaxDepth);
        rayTracingMaterial.SetInt("accumulateBVHColors", accumulateBVHColors ? 1 : 0);

        // Debug Info
        rayTracingMaterial.SetInt("ShowIntersectionCount", showIntersectionCount ? 1 : 0);
        rayTracingMaterial.SetInt("maxIntersectionTests", maxIntersectionTests);

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

        // View Parameters
        rayTracingMaterial.SetVector("ViewParams", viewParams);
        rayTracingMaterial.SetMatrix("CamLocalToWorldMatrix", CamLocalToWorldMatrix);  
	}


    // Called when the script instance is being loaded
    void OnDisable() {
        ShaderHelper.Release(meshInfoBuffer);
        ShaderHelper.Release(triangleInfoBuffer);
        ShaderHelper.Release(sphereInfoBuffer);
        ShaderHelper.Release(stencilInfoBuffer);
        ShaderHelper.Release(roomInfoBuffer);
        ShaderHelper.Release(lightInfoBuffer);
        ShaderHelper.Release(bvhNodeInfoBuffer);
        ShaderHelper.Release(wrapperInfoBuffer);
    }

    void OnDestroy() {
    }

    // Called when the script is loaded or a value is changed in the inspector (Called in the editor only)
    void OnValidate() {
		maxBounceCount = Mathf.Max(0, maxBounceCount);
		numRaysPerPixel = Mathf.Max(1, numRaysPerPixel);
		sunFocus = Mathf.Max(1, sunFocus);
		sunIntensity = Mathf.Max(0, sunIntensity);

        fpsCounter = FindObjectOfType<FPSCounter>();
        if(fpsCounter != null) {
            fpsCounter.toggleable(showFPSCounter); 
        }

        //InitFrame(); // Reinitialize the frame when a value is changed in the inspector
	}
    
    void Start(){
        singleLayer = false;
        connected = false;
    }


    void SanityChecks(){

        if(singleLayer){
            nextLayer = currentLayer; // If single layer, next layer is the same as current layer
            connected = false;
        }

        if(connected){
            nextLayer = currentLayer + 1;
            singleLayer = false;
        }

        if(currentLayer >= numRooms) currentLayer = numRooms - 1;
        if(currentLayer < 0) currentLayer = 0;

        if(nextLayer >= numRooms) nextLayer = numRooms - 1;
        if(nextLayer < 0) nextLayer = 0;


    }


}
