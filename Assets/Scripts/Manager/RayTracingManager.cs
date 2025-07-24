using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Mathf;
using UnityEngine.Rendering;
using System.Diagnostics;

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
    [SerializeField] bool skipConstruction; // If true, the BVH will use colors for each depth level
    [SerializeField] bool showBVHDepth;  // if true, it will show the bounding box of the depth of the BVH
    [SerializeField] bool accumulateBVHColors; // If true, the BVH will accumulate colors for each depth level
    [SerializeField, Range(0, 16)] int bvhDepth = 1; // Depth of the BVH tree, 0 means no BVH
    [SerializeField] int bvhMaxDepth = 16; // Scale of the bounding box
    [ReadOnly] [SerializeField] int bvhMaxDepthReached;
    [ReadOnly] [SerializeField] int numBVHNodes;
    [ReadOnly] [SerializeField] int numWrapperObjects; 

    [Header("Stencil Buffer Info")]
    [SerializeField, Range(1,2)] int currentLayer = 1;

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


    // View Parameters
    Vector3 viewParams;
    Matrix4x4 CamLocalToWorldMatrix;

    // --- Materials ---
    Material rayTracingMaterial;  

    // --- Lists --- 
    RoomObject[] roomObjects;
    MeshObject[] meshObjects;
    SphereObject[] sphereObjects;
    StencilObject[] stencilObjects; 

    // --- Info Lists to send to shader ---
    RoomInfo[] roomInfos;
    SphereInfo[] sphereInfos;
    StencilInfo[] stencilInfos;
    MeshInfo[] meshInfos;
    List<TriangleInfo> triangleInfos;
    List<LightInfo> lightInfos;

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
        bool shouldApplyShader = Camera.current.name != "SceneCamera" || useShaderInSceneView;
        if (shouldApplyShader && (useRayTracing || useSimpleShape || useImportanceSampling)) {
            InitFrame();
            Graphics.Blit(null, target, rayTracingMaterial);  // Run the ray tracing shader and draw the result to the screen
        }  else {
            Graphics.Blit(src, target); // Copy the source texture to the target texture i.e. do not apply the shader
        }
    }

    void InitFrame(){
		ShaderHelper.InitMaterial(rayTracingShader, ref rayTracingMaterial); // Create materials used in blits
        UpdateCameraParams(Camera.current);

        
        UnityEngine.Debug.Log($"======= Camera: {Camera.current.name}" );

        var sw1 = Stopwatch.StartNew();
        CreateObjectlists();
        sw1.Stop();
        UnityEngine.Debug.Log($"CreateObjectLists: {sw1.ElapsedMilliseconds} ms");

        var sw2 = Stopwatch.StartNew();
        CreateBVH(); // Create the BVH for the objects in the scene
        sw2.Stop();
        UnityEngine.Debug.Log($"CreateBVH: {sw2.ElapsedMilliseconds} ms");

        var sw3 = Stopwatch.StartNew();
        AssignInfoLists(); // Assign the info lists to the arrays
        sw3.Stop();
        UnityEngine.Debug.Log($"AssignInfoLists: {sw3.ElapsedMilliseconds} ms");
        
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


    // Loop through all objects (meshes and spheres) and create a arrays of rooms and objects, then send data to the shader
    // There is no optimization here, just rooms with objects in the rooms.
    void CreateObjectlists(){

        roomObjects = FindObjectsOfType<RoomObject>();
        numRooms = roomObjects.Length;
        System.Array.Sort(roomObjects, (a, b) => a.layer.CompareTo(b.layer));

        // -- Initialize the rooms 
        for(int i = 0; i < numRooms; i++){ 
            roomObjects[i].layer = i+1; // layers start at 1, not 0
            roomObjects[i].numSpheres = 0;
            roomObjects[i].numMeshes = 0; 
            roomObjects[i].numStencils = 0;
        }

        meshObjects = FindObjectsOfType<MeshObject>();
        sphereObjects = FindObjectsOfType<SphereObject>();
        stencilObjects = FindObjectsOfType<StencilObject>();

        // Set each child object's layer to match its parent room
        foreach (var mesh in meshObjects) {
            RoomObject parent = mesh.GetComponentInParent<RoomObject>();
            if (parent != null) mesh.layer = parent.layer;
        }

        foreach (var sphere in sphereObjects) {
            RoomObject parent = sphere.GetComponentInParent<RoomObject>();
            if (parent != null) sphere.layer = parent.layer;
        }

        System.Array.Sort(meshObjects, (a, b) => a.layer.CompareTo(b.layer));
        System.Array.Sort(sphereObjects, (a, b) => a.layer.CompareTo(b.layer));
        System.Array.Sort(stencilObjects, (a, b) => a.layer.CompareTo(b.layer));
        
        numStencils = stencilObjects.Length;
        numSpheres = sphereObjects.Length;
        numMeshes = meshObjects.Length;

        // -- Reset but Not assigned here --
	    numTriangles = 0;
        numLights = 0;

        triangleInfos = new List<TriangleInfo>(); ;
        lightInfos = new List<LightInfo>();

        // -- Create meshInfos (and lights) and add to rooms:
        int triangleStartIndex = 0; // Start index for triangles in mesh and room
        int prevLayer = -1; // Previous layer to check for changes
        for(int i = 0; i < numMeshes; i++){
            MeshObject meshObject = meshObjects[i];
            meshObject.InitializeTrianglesAndBounds();
            meshObject.triangleStartIndex = triangleStartIndex; // Set the start index for triangles in this mesh
            meshObject.meshIndex = i;

            List<TriangleInfo> triangles = meshObject.GetTriangles();
            triangleInfos.AddRange(triangles);

            int triangleCount = triangles.Count;
            meshObject.triangleCount = triangleCount;
            numTriangles += triangleCount;

            int layer = meshObject.layer;
            if(prevLayer!= layer) {
                roomObjects[layer-1].trianglesIndex = triangleStartIndex; 
                prevLayer = layer;
            }
            roomObjects[layer-1].numMeshes++;  
            roomObjects[layer-1].numTriangles += triangleCount;  
            

            triangleStartIndex += meshObject.triangleCount; // Update the start index for the next mesh

            // Add to lights if emissive
            if(meshObject.isLightSource){
                lightInfos.Add(new LightInfo{ 
                    position = meshObject.GetCenter(), 
                    radius = meshObject.GetMaxVertexDistanceFromCenter(),
                    layer = layer
                 });
                numLights++;
            }        
        }

        // -- Add spheres to Rooms:
        for(int i = 0; i < numSpheres; i++){
            SphereObject sphereObject = sphereObjects[i];
            sphereObject.calculateBounds();
            int layer = sphereObject.layer;
            roomObjects[layer-1].numSpheres++;

            // Add to lights if emissive            
            if (sphereObject.isLightSource){ 
                lightInfos.Add(new LightInfo { 
                    position = sphereObject.transform.position, 
                    radius = sphereObject.getRadius(),
                    layer = layer
                });
                numLights++;
            }
        }

        // -- Add stencils to Rooms:
        for(int i = 0; i < numStencils; i++) {
            stencilObjects[i].ExtractQuadParameters();
            int layer = stencilObjects[i].layer;
            roomObjects[layer-1].numStencils++;
            roomObjects[layer-1].stencilIndex = i;
        }

        // -- Add Index to Rooms --
        if(numRooms > 0) {
            roomObjects[0].meshIndex = 0;
            roomObjects[0].spheresIndex = 0;
            roomObjects[0].stencilIndex = 0;
        }

        for(int i = 1; i < numRooms; i++){
            roomObjects[i].meshIndex = roomObjects[i-1].numMeshes + roomObjects[i-1].meshIndex;
            roomObjects[i].spheresIndex = roomObjects[i-1].numSpheres + roomObjects[i-1].spheresIndex;
            roomObjects[i].stencilIndex = roomObjects[i-1].numStencils + roomObjects[i-1].stencilIndex;
        }

    }


    void CreateBVH() {
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
            BVH bvh = new BVH(meshObjects, roomObjects[r].meshIndex, roomObjects[r].numMeshes, sphereObjects, roomObjects[r].spheresIndex, roomObjects[r].numSpheres, bvhMaxDepth, roomObjects[r].layer, useFullObjectsInBVH);

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

    void AssignInfoLists(){
        roomInfos = new RoomInfo[numRooms];
        sphereInfos = new SphereInfo[numSpheres];
        stencilInfos = new StencilInfo[numStencils];
        meshInfos = new MeshInfo[numMeshes];
        wrapperInfos = new WrapperInfo[numWrapperObjects];
        bvhNodeInfos = new BVHNodeInfo[numBVHNodes];

        // Should already be initialized: 
        // triangleInfos = new List<TriangleInfo>();
        // lightInfos = new List<LightInfo>();
        for(int i = 0; i < numRooms; i++){
            roomInfos[i] = new RoomInfo();
            roomInfos[i].layer = roomObjects[i].layer;
            roomInfos[i].meshIndex = roomObjects[i].meshIndex;
            roomInfos[i].numMeshes = roomObjects[i].numMeshes;
            roomInfos[i].spheresIndex = roomObjects[i].spheresIndex;
            roomInfos[i].numSpheres = roomObjects[i].numSpheres;
            roomInfos[i].stencilIndex = roomObjects[i].stencilIndex;
            roomInfos[i].numStencils = roomObjects[i].numStencils;
            roomInfos[i].numWrappers = roomObjects[i].numWrappers;
            roomInfos[i].wrappersIndex = roomObjects[i].wrappersIndex;
            roomInfos[i].numbvhNodes = roomObjects[i].numbvhNodes;
            roomInfos[i].bvhNodesIndex = roomObjects[i].bvhNodesIndex;
        }   

        for(int i = 0; i < numMeshes; i++){
            meshInfos[i] = new MeshInfo();
            meshInfos[i].triangleStartIndex = meshObjects[i].triangleStartIndex;
            meshInfos[i].triangleCount = meshObjects[i].triangleCount;
            meshInfos[i].material = meshObjects[i].material;
            meshInfos[i].boundsMin = meshObjects[i].GetBounds().min;
            meshInfos[i].boundsMax = meshObjects[i].GetBounds().max;
            meshInfos[i].layer = meshObjects[i].layer;
        }

        for(int i = 0; i < numSpheres; i++){
            sphereInfos[i] = new SphereInfo();
            sphereInfos[i].position = sphereObjects[i].transform.position;
            sphereInfos[i].radius = sphereObjects[i].transform.localScale.x * 0.5f;
            sphereInfos[i].material = sphereObjects[i].material;
            sphereInfos[i].layer = sphereObjects[i].layer;
        }

        for(int i = 0; i < numStencils; i++){
            stencilInfos[i] = new StencilInfo();
            stencilInfos[i].center = stencilObjects[i].GetCenter();
            stencilInfos[i].normal = stencilObjects[i].GetNormal();
            stencilInfos[i].u = stencilObjects[i].GetU();
            stencilInfos[i].v= stencilObjects[i].GetV();
            stencilInfos[i].layer = stencilObjects[i].layer;
            stencilInfos[i].nextLayer = stencilObjects[i].nextLayer;
        }


        // These have to be global indices for the lists, therefore we need to offset the indices by the room index
        for(int i = 0; i < numBVHNodes; i++){
            bvhNodeInfos[i] = new BVHNodeInfo {
                minBounds = bvhNodes[i].minBounds,
                maxBounds = bvhNodes[i].maxBounds,
                isLeaf = bvhNodes[i].isLeaf ? 1 : 0,
                startWrapperIndex = bvhNodes[i].startWrapperIndex + roomObjects[bvhNodes[i].layer-1].wrappersIndex, // Offset by the room index
                lengthOfWrappers = bvhNodes[i].lengthOfWrappers,
                leftChildIndex = bvhNodes[i].isLeaf? -1 :  bvhNodes[i].leftChildIndex + roomObjects[bvhNodes[i].layer-1].bvhNodesIndex, // Offset by the room index
                rightChildIndex = bvhNodes[i].isLeaf? -1 : bvhNodes[i].rightChildIndex + roomObjects[bvhNodes[i].layer-1].bvhNodesIndex // Offset by the room index
            };
        }


        
        for(int i = 0; i < numWrapperObjects; i++){
            wrapperInfos[i] = new WrapperInfo {
                minBounds = wrapperObjects[i].minBounds,
                maxBounds = wrapperObjects[i].maxBounds,
                isTriangle = wrapperObjects[i].isTriangle ? 1 : 0,
                meshIndex = wrapperObjects[i].isTriangle ? wrapperObjects[i].meshIndex + roomObjects[wrapperObjects[i].layer-1].meshIndex : -1, // mesh index in wrapper + Offset by the room mesh index ( or -1 if it's a sphere)
                index =  wrapperObjects[i].index + (wrapperObjects[i].isTriangle ? roomObjects[wrapperObjects[i].layer-1].trianglesIndex :  roomObjects[wrapperObjects[i].layer-1].spheresIndex) // index of triangle or sphere in wrapper + Offset by the room index               
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
        fpsCounter.toggleable(showFPSCounter); 

        //InitFrame(); // Reinitialize the frame when a value is changed in the inspector
	}


}
