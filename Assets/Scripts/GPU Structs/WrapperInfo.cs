using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct WrapperInfo {
    public Vector3 minBounds; // minimum corner of the bounding box
    public Vector3 maxBounds; // maximum corner of the bounding box
    public int isTriangle; // 1 if triangle, 0 if sphere
    public int meshIndex; // index of the mesh this triangle belongs to, or -1 if it's a sphere (global index)
    public int index; // index of triangle (in mesh) or sphere (in all spheres list) (global index)

    // global index is used to identify the triangle or sphere in the global list of all triangles and spheres
}
