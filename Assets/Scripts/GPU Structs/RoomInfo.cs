using UnityEngine;

public struct RoomInfo {
    public int layer;
    public int globalMeshesIndex;
    public int numMeshes;
    public int globalSpheresIndex;    
    public int numSpheres;
    public int globalStencilsIndex;
    public int numStencils;
    
    public int numWrappers; // Total number of wrappers (triangles + spheres)
    public int wrappersIndex; // Index of the first wrapper in the list
    public int numbvhNodes; // Total number of BVH nodes
    public int bvhNodesIndex; // Index of the first BVH node in the list
}
