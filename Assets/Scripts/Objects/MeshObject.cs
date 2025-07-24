using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeshObject : MonoBehaviour {

    [Header("Settings")]
    public MeshFilter meshFilter;
    public MeshRenderer meshRenderer;
    public Mesh mesh;

    public int meshIndex; // index of the mesh in the total list

    [ReadOnly] public int triangleStartIndex;
    [ReadOnly] public int triangleCount;

    public RayTracingMaterial material;

    [ReadOnly] public bool isLightSource;
    [ReadOnly] public Vector3 center;

    List<TriangleInfo> localTriangles;
    List<TriangleInfo> worldTriangles;

    [ReadOnly] public Vector3 boundsMax;
    [ReadOnly] public Vector3 boundsMin;
    private Bounds bounds;
    
    [Header("Stencil Buffer Info")]
	public int layer = 1;

    private void OnValidate() {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        if(meshFilter == null || meshRenderer == null || meshFilter.sharedMesh == null) {
            Debug.LogError("Some mesh is not assigned."); return;
        }

        mesh = meshFilter.sharedMesh;
        InitializeTrianglesAndBounds();
        updateMaterial();

        isLightSource = material.emissionStrength > 0f && material.emissionColor.maxColorComponent > 0f;

        center = GetCenter();
    }

    public void InitializeTrianglesAndBounds() {
        worldTriangles ??= new List<TriangleInfo>();
        worldTriangles.Clear();

        meshFilter = GetComponent<MeshFilter>();
        mesh = meshFilter.sharedMesh;

        // Convert data from local to world space
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        Vector3 scale = transform.lossyScale;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int[] localTriangles = mesh.triangles;

        triangleCount = mesh.triangles.Length / 3;

        Vector3 first = vertices[localTriangles[0]];

        Vector3 boundsMin = PointLocalToWorld( first, pos, rot, scale);
		Vector3 boundsMax = boundsMin;

        for (int i = 0; i < localTriangles.Length; i += 3) {
            // Get local vertices and normals
            Vector3 v0 = vertices[localTriangles[i]];
            Vector3 v1 = vertices[localTriangles[i + 1]];
            Vector3 v2 = vertices[localTriangles[i + 2]];
            Vector3 n0 = normals[localTriangles[i]];
            Vector3 n1 = normals[localTriangles[i + 1]];
            Vector3 n2 = normals[localTriangles[i + 2]];

            // Convert to world space
            Vector3 worldv0 = PointLocalToWorld(v0, pos, rot, scale);
            Vector3 worldv1 = PointLocalToWorld(v1, pos, rot, scale);
            Vector3 worldv2 = PointLocalToWorld(v2, pos, rot, scale);
            Vector3 worldn0 = DirectionLocalToWorld(n0, rot);
            Vector3 worldn1 = DirectionLocalToWorld(n1, rot);
            Vector3 worldn2 = DirectionLocalToWorld(n2, rot);

            // Create wold triangle
            TriangleInfo triangle = new TriangleInfo(worldv0, worldv1, worldv2, worldn0, worldn1, worldn2, meshIndex);
            worldTriangles.Add(triangle);

            boundsMin = Vector3.Min(boundsMin, worldv0);
			boundsMax = Vector3.Max(boundsMax, worldv0);
			boundsMin = Vector3.Min(boundsMin, worldv1);
			boundsMax = Vector3.Max(boundsMax, worldv1);
			boundsMin = Vector3.Min(boundsMin, worldv2);
			boundsMax = Vector3.Max(boundsMax, worldv2);
        }
        bounds = new Bounds((boundsMin + boundsMax) / 2, boundsMax - boundsMin);

        this.boundsMin = boundsMin;
        this.boundsMax = boundsMax;
    }

    public Vector3 GetCenter() {
        List<TriangleInfo> triangles = GetTriangles();
        Vector3 center = Vector3.zero;
        int totalVertices = triangles.Count * 3;
        for (int i = 0; i < triangles.Count; i++) {
            center += triangles[i].v0 + triangles[i].v1 + triangles[i].v2;
        }
        center /= totalVertices;
        return center;
    }


    public List<TriangleInfo> GetTriangles() {
        InitializeTrianglesAndBounds();
        return worldTriangles;
    }

    public Bounds GetBounds() {
        InitializeTrianglesAndBounds();
        return bounds;
    }

	static Vector3 PointLocalToWorld(Vector3 p, Vector3 pos, Quaternion rot, Vector3 scale) {
		return rot * Vector3.Scale(p, scale) + pos;
	}

	static Vector3 DirectionLocalToWorld(Vector3 dir, Quaternion rot) {
		return rot * dir;
	}

    public float GetMaxVertexDistanceFromCenter() {
        List<TriangleInfo> triangles = GetTriangles();
        float maxDistance = 0f;

        foreach (var tri in triangles) {
            maxDistance = Mathf.Max(maxDistance, (tri.v0 - center).magnitude);
            maxDistance = Mathf.Max(maxDistance, (tri.v1 - center).magnitude);
            maxDistance = Mathf.Max(maxDistance, (tri.v2 - center).magnitude);
        }

        return maxDistance;
    }



    void updateMaterial() {
        if (meshRenderer == null) return;

        Material unityMat = meshRenderer.sharedMaterial;
        if (unityMat == null) return;

        // Copy base color
        if (unityMat.HasProperty("_Color")) {
            material.color = unityMat.color;
        } 
    }

}
