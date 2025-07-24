using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RoomObject : MonoBehaviour {
    
    public int layer;
    // This is the index of the room in the global room list. Normally, it would be the layer, but if a room is missing then this is the index it will take on to avoid crashes 
    [ReadOnly] [SerializeField] public int virtualizedLayer; // Index of this room in the global list of rooms


    [Header("Number of Objects")]
    // These are the local totals of this room 
    [ReadOnly] [SerializeField] public int numMeshes;
    [ReadOnly] [SerializeField] public int numTriangles;
    [ReadOnly] [SerializeField] public int numSpheres;
    [ReadOnly] [SerializeField] public int numStencils;
    [ReadOnly] [SerializeField] public int numLights; 
    [ReadOnly] [SerializeField] public int numCubes; // number of cubes in the room, used for debugging and future features

    [Header("Global Indices of Objects")]
    // These are automatically updated in the Ray Tracing Manager, after all of the local lists have been processed
    [ReadOnly] [SerializeField] public int globalMeshesIndex; // Index of the first mesh in the global mesh list
    [ReadOnly] [SerializeField] public int globalTrianglesIndex; // Index of the first triangle in the global triangle list
    [ReadOnly] [SerializeField] public int globalSpheresIndex; // Index of the first sphere in the global sphere list 
    [ReadOnly] [SerializeField] public int globalStencilsIndex; // Index of the first stencil in the stencil list
    // Lights:  we always search for all the lights in the scene, so we don't keep a global index per room
    [ReadOnly] [SerializeField] public int globalCubesIndex; // Index of the first cube in the global cube list, used for debugging and future features

    [Header("Room BVH")]
    // BVH 
    [ReadOnly] [SerializeField] public int numWrappers; // Total number of wrappers (triangles + spheres)
    [ReadOnly] [SerializeField] public int wrappersIndex; // Index of the first wrapper in the list

    [ReadOnly] [SerializeField] public int numbvhNodes; // Total number of BVH nodes
    [ReadOnly] [SerializeField] public int bvhNodesIndex; // Index of the first BVH node in the list


    


    // These are the local lists for this room.
    // Note: Only Meshes,Spheres and Stencils actually exist. Triangles and Lights are really actually properties. 
    public List<MeshObject> meshObjects;
    public List<TriangleObject> triangleObjects; 
    public List<SphereObject> sphereObjects;
    public List<StencilObject> stencilObjects;
    public List<LightObject> lightObjects; 
    public List<CubeObject> cubeObjects; // List of cube objects in the room, used for debugging and future features

    
    [Header("Debug")]
    [SerializeField] public bool isActive = true; // Whether this room is active in the ray tracing process

    // Called by the RayTracingManager, it processes all of the objects in the room:
    // - Assigns all lists
    // - Assigns all counts
    // - DOES NOT assigned global indices
    public void processObjects(){

        // Reset all counts
        numMeshes = 0;
        numTriangles = 0;
        numSpheres = 0;
        numStencils = 0;
        numLights = 0;
        numCubes = 0; 

        // reset all indices
        globalMeshesIndex = 0;
        globalTrianglesIndex = 0;
        globalSpheresIndex = 0;
        globalStencilsIndex = 0;
        globalCubesIndex = 0;

        // maybe do later
        wrappersIndex = 0; // Index of the first wrapper in the list
        numbvhNodes = 0; // Total number of BVH nodes
        numWrappers = 0; // Total number of wrappers (triangles + spheres)
        bvhNodesIndex = 0; // Index of the first BVH node in the list

        meshObjects = new List<MeshObject>();
        triangleObjects = new List<TriangleObject>();
        sphereObjects = new List<SphereObject>();
        stencilObjects = new List<StencilObject>();
        lightObjects = new List<LightObject>();
        cubeObjects = new List<CubeObject>(); 

        processMeshes();
        processSpheres();
        processStencils();
        processCubes();
    }

    private void processMeshes(){
        // We have a local indexing system for meshObjects. Then when we turn into meshInfos it becomes global.
        meshObjects = GetMeshObjectsInThisRoom();
        numMeshes = meshObjects.Count; 
        for (int i = 0; i < numMeshes; i++) {

            // 1) Get mesh and initialize its properties 
            MeshObject meshObject = meshObjects[i];
            meshObject.layer = layer; // Make sure we have the layer set correctly
            meshObject.virtualizedLayer = virtualizedLayer; 
            meshObject.localTriangleStartIndex = numTriangles; // Set the start index for triangles in this mesh object;
            meshObject.InitializeTrianglesAndBounds(); // Initialize triangles and bounds for each mesh object

            // 2) Get triangles of the mesh
            List<TriangleObject> triangles = meshObject.GetTriangleObjects(); // Get the triangle objects from the mesh object
            // Note: the localMeshIndex of the triangle is already set, at the creation of the MeshObject. 

            // 3) Add count 
            numTriangles += triangles.Count;

            // 4) We set the localMeshesIndex of the triangles
            // This should be done already, but just to be safe we set it here again.
            for(int j = 0; j < triangles.Count; j++) triangles[j].localMeshesIndex = i;
            
            // 4) Add triangles to local list 
            triangleObjects.AddRange(triangles); // Add the triangle objects to the local list

            // 5) Add to local lights list if the mesh is emissive 
            if(meshObject.isLightSource){
                lightObjects.Add(new LightObject( meshObject.GetCenter(), meshObject.GetMaxVertexDistanceFromCenter(), layer));
                numLights++;
            }   
        }

    }

    private void processSpheres(){
        sphereObjects = GetSphereObjects();
        numSpheres = sphereObjects.Count;
        for(int i = 0; i < numSpheres; i++){

            // 1) Get sphere and initialize its properties
            SphereObject sphereObject = sphereObjects[i];
            sphereObject.layer = layer; // Set the layer for the sphere object
            sphereObject.virtualizedLayer = virtualizedLayer; // layer of the virtualized room
            sphereObject.calculateBounds();

            // 2) Add to lights if emissive            
            if (sphereObject.isLightSource){ 
                lightObjects.Add(new LightObject ( sphereObject.transform.position,  sphereObject.getRadius(),  layer));
                numLights++;
            }
        }

    }

    private void processStencils(){
        stencilObjects = GetStencilObjects();
        numStencils = stencilObjects.Count;

        for(int i = 0; i < numStencils; i++){
            StencilObject stencilObject = stencilObjects[i];
            stencilObject.layer = layer; // Make sure the layer is correct
            stencilObject.virtualizedLayer = virtualizedLayer;
            stencilObject.ExtractQuadParameters(); // Extract the quad parameters for the stencil object

        }
    }


    private void processCubes(){
        cubeObjects = new List<CubeObject>(this.GetComponentsInChildren<CubeObject>());
        numCubes = cubeObjects.Count;

        for(int i = 0; i < numCubes; i++){
            CubeObject cubeObject = cubeObjects[i];
            cubeObject.layer = layer; 
            cubeObject.virtualizedLayer = virtualizedLayer;
            cubeObject.UpdateCubeData(); 
            if(cubeObject.isLightSource) {
                lightObjects.Add(new LightObject(cubeObject.center, cubeObject.GetDistanceFromCenterToVertex(), layer));
                numLights++;
            }
        }
    }



    void OnValidate() {
        foreach (Transform child in transform) {
            child.gameObject.SetActive(isActive);
        }

        if(meshObjects != null && sphereObjects != null && stencilObjects != null){
            numMeshes = meshObjects.Count;
            numTriangles = 0;
            numSpheres = sphereObjects.Count;
            numStencils = stencilObjects.Count;
            
            for (int i = 0; i < numMeshes; i++) {
                numTriangles += meshObjects[i].triangleCount;
            }
        }
    }

    public void setActive(bool active) {
        isActive = active;
        foreach (Collider col in GetComponentsInChildren<Collider>()) {
            col.enabled = active;
        }
    }





    private List<MeshObject> GetMeshObjectsInThisRoom( ) {
        return new List<MeshObject>(this.GetComponentsInChildren<MeshObject>());
    }

    private List<SphereObject> GetSphereObjects( ) {
        return new List<SphereObject>(this.GetComponentsInChildren<SphereObject>());
    }

    private List<StencilObject> GetStencilObjects( ) {
        return new List<StencilObject>(this.GetComponentsInChildren<StencilObject>());
    }







}
