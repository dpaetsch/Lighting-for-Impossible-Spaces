using UnityEngine;

public class StencilObject : MonoBehaviour {

    [Header("Info")]
    public MeshFilter meshFilter;
    [SerializeField] Mesh mesh;

    [Header("Stencil Buffer Info")]
    public int layer = 1;
    public int nextLayer = 2; 

    [Header("Quad Parameters")]
    [ReadOnly] [SerializeField] private Vector3 center; // Center position of the rectangle
    [ReadOnly] [SerializeField] private Vector3 normal; // Normal vector defining orientation
    [ReadOnly] [SerializeField] private Vector3 u; // First basis vector (width direction)
    [ReadOnly] [SerializeField] private Vector3 v; // Second basis vector (height direction)

    void OnValidate() {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        mesh = meshFilter.sharedMesh;
        ExtractQuadParameters();
	}
    
    public void ExtractQuadParameters() {
        if(meshFilter == null) {Debug.LogError("MeshFilter is not assigned."); return;}

        // Get Transform (for world-space conversion)
        Transform quadTransform = meshFilter.transform;

        // Get local vertices
        Vector3[] localVertices = mesh.vertices;

        // Convert to world space
        Vector3 v0 = quadTransform.TransformPoint(localVertices[0]); // Bottom-left
        Vector3 v1 = quadTransform.TransformPoint(localVertices[1]); // Bottom-right
        Vector3 v2 = quadTransform.TransformPoint(localVertices[2]); // Top-left
        Vector3 v3 = quadTransform.TransformPoint(localVertices[3]); // Top-right
        center = (v0 + v1 + v2 + v3) / 4f;

        // Basis Vectors 
        // u = v1 - v0; // Width direction
        // v = v2 - v0; // Height direction
        // Transform local basis vectors to world space (helps with non-uniform scaling, rounding errors)
        u = quadTransform.right * quadTransform.localScale.x;  // Width direction
        v = quadTransform.up * quadTransform.localScale.y;    // Height direction

        normal = Vector3.Cross(u, v).normalized;

        /* Only if necessary
        // Ensure small floating-point errors are removed
        center = new Vector3(Mathf.Round(center.x * 100000f) / 100000f, 
                            Mathf.Round(center.y * 100000f) / 100000f, 
                            Mathf.Round(center.z * 100000f) / 100000f);
        */
    }


    public Vector3 GetCenter() {
        ExtractQuadParameters();
        return center;
    }

    public Vector3 GetNormal() {
        ExtractQuadParameters();
        return normal;
    }

    public Vector3 GetU() {
        ExtractQuadParameters();
        return u;
    }

    public Vector3 GetV() {
        ExtractQuadParameters();
        return v;
    }
}
