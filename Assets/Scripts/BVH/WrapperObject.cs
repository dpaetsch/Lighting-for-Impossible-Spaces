using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WrapperObject {

    // The idea is that we have a wrapper object assigned for each triangle and sphere in a scene.
    // It contains the bounding box of the triangle or sphere, and the index of the triangle or sphere in their respective lists.
    // This allows us to indirectly access the specific triangle or sphere in a list, and also be able to sort
    // the wrapper objects based on their bounding boxes for efficient BVH construction, without changing the original lists.
    
    // Relative to room means that the indices start from 0 in local rooms, but are differnet in the global context.

    public Vector3 minBounds; // minimum corner of the bounding box
    public Vector3 maxBounds; // maximum corner of the bounding box

    public bool isTriangle; // true if this node contains a triangle, false if it contains a sphere 
    // if instead there is an object, isTriangle indicates whether it is a mesh object or a sphere object


    public int meshIndex; // index of the mesh this triangle belongs to, or -1 if it's a sphere (relative to room)
    public int index; // index of triangle (in mesh) or sphere (in all spheres list) (relative to room)
    public Vector3 center;  // center of triangle or sphere

    public int layer; 

    public WrapperObject(TriangleObject triangle, int meshIndex, int index) {
        minBounds = triangle.v0;
        maxBounds = triangle.v0;
        minBounds = Vector3.Min(minBounds, triangle.v1);
        minBounds = Vector3.Min(minBounds, triangle.v2);
        maxBounds = Vector3.Max(maxBounds, triangle.v1);
        maxBounds = Vector3.Max(maxBounds, triangle.v2);
        isTriangle = true;
        this.meshIndex = meshIndex;
        this.index = index;
        center = (minBounds + maxBounds) / 2;

    }

    public WrapperObject(MeshObject meshObject, int index) {
        Bounds bounds = meshObject.GetBounds();
        minBounds = meshObject.boundsMin;
        maxBounds = meshObject.boundsMax;
        isTriangle = true; // since this wrapper is for a mesh object
        this.index = index; // has no meaning when it's a wrapper for a mesh object, but we keep it for consistency
        this.meshIndex = index; // index of the mesh in the total list
        center = (minBounds + maxBounds) / 2;
        layer = meshObject.layer;
    }


    public WrapperObject(SphereObject sphere, int index) {
        minBounds = sphere.boundsMin;
        maxBounds = sphere.boundsMax;
        isTriangle = false;
        this.index = index;
        this.meshIndex = -1; // -1 since it's a sphere, not a triangle
        center = (minBounds + maxBounds) / 2;
    }




}
