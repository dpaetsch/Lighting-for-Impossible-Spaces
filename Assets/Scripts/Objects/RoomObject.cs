using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RoomObject : MonoBehaviour {
    
    public int layer;

    [ReadOnly] [SerializeField] public int numMeshes;
    [ReadOnly] [SerializeField] public int meshIndex; // Index of the first mesh in the list

    [ReadOnly] [SerializeField] public int numTriangles;
    [ReadOnly] [SerializeField] public int trianglesIndex; // Index of the first triangle in the list

    [ReadOnly] [SerializeField] public int numSpheres;
    [ReadOnly] [SerializeField] public int spheresIndex; // Index of the first sphere in the list 

    [ReadOnly] [SerializeField] public int numStencils;
    [ReadOnly] [SerializeField] public int stencilIndex; // Index of the first stencil in the list

    [ReadOnly] [SerializeField] public int numWrappers; // Total number of wrappers (triangles + spheres)
    [ReadOnly] [SerializeField] public int wrappersIndex; // Index of the first wrapper in the list

    [ReadOnly] [SerializeField] public int numbvhNodes; // Total number of BVH nodes
    [ReadOnly] [SerializeField] public int bvhNodesIndex; // Index of the first BVH node in the list

    List<MeshObject> meshObjects;
    List<SphereObject> sphereObjects;
    List<StencilObject> stencilObjects;


    [Header("Debug")]
    [SerializeField] public bool isActive = true; // Whether this room is active in the ray tracing process


    void OnValidate() {
        foreach (Transform child in transform) {
            child.gameObject.SetActive(isActive);
        }


        if (meshObjects == null) meshObjects = new List<MeshObject>();
        if (sphereObjects == null) sphereObjects = new List<SphereObject>();
        if (stencilObjects == null) stencilObjects = new List<StencilObject>();

        numMeshes = meshObjects.Count;
        numTriangles = 0;
        numSpheres = sphereObjects.Count;
        numStencils = stencilObjects.Count;

        for (int i = 0; i < numMeshes; i++) {
            numTriangles += meshObjects[i].triangleCount;
        }

    }

    public void setActive(bool active) {
        isActive = active;
        foreach (Collider col in GetComponentsInChildren<Collider>()) {
            col.enabled = active;
        }
    }

}
