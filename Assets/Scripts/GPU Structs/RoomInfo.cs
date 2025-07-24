using UnityEngine;

public struct RoomInfo {
    public int layer;
    public int meshIndex;
    public int numMeshes;
    public int spheresIndex;    
    public int numSpheres;
    public int stencilIndex;
    public int numStencils;
    public int numWrappers; // Total number of wrappers (triangles + spheres)
    public int wrappersIndex; // Index of the first wrapper in the list
    public int numbvhNodes; // Total number of BVH nodes
    public int bvhNodesIndex; // Index of the first BVH node in the list
}
