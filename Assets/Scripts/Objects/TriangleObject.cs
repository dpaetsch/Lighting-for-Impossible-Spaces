using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TriangleObject {

    // Vertices
    public Vector3 v0;
    public Vector3 v1;
    public Vector3 v2;

    // Normals
    public Vector3 n0;
    public Vector3 n1;
    public Vector3 n2;

    // Indices
    public int localMeshesIndex; // Index of the mesh this triangle belongs to, in the local list
    public int globalMeshesIndex; // Index of the mesh this triangle belongs to, in the global list (set in RayTracingManager)
    // note: globalMeshesIndex is only so that we can relocate the original mesh so that we can get its material when we intersect with this triangle.
    
    public TriangleObject(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 n0, Vector3 n1, Vector3 n2) {
        this.v0 = v0;
        this.v1 = v1;
        this.v2 = v2;
        this.n0 = n0;
        this.n1 = n1;
        this.n2 = n2;
    }
}
