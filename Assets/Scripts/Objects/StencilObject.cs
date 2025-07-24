using UnityEngine;

public class StencilObject : MonoBehaviour {
    [Header("Settings")]
    public RayTracingMaterial material;

    [Header("Info")]
    public MeshRenderer meshRenderer;
    public MeshFilter meshFilter;
    [SerializeField, HideInInspector] int materialObjectID;
	[SerializeField, HideInInspector] bool materialInitFlag;
    [SerializeField] Mesh mesh;

    [Header("Quad Parameters")]
    public Vector3 center; // Center position of the rectangle
    public Vector3 normal; // Normal vector defining orientation
    public Vector3 u; // First basis vector (width direction)
    public Vector3 v; // Second basis vector (height direction)

    
    
    [Header("Stencil Buffer Info")]
    public int layer = 1;
    public int nextLayer = 2; 



    public void ExtractQuadParameters() {
        if (meshFilter == null) {
            Debug.LogError("MeshFilter not assigned!");
            return;
        }

        Mesh mesh = meshFilter.sharedMesh;
        if (mesh.vertexCount != 4)  {
            Debug.LogError("This is not a quad!");
            return;
        }

        // Get Transform (for world-space conversion)
        Transform quadTransform = meshFilter.transform;

        // Get local vertices
        Vector3[] localVertices = mesh.vertices;

        //Debug.Log($"Local Vertices: {localVertices[0]}, {localVertices[1]}, {localVertices[2]}, {localVertices[3]}");

        // Convert to world space
        Vector3 v0 = quadTransform.TransformPoint(localVertices[0]); // Bottom-left
        Vector3 v1 = quadTransform.TransformPoint(localVertices[1]); // Bottom-right
        Vector3 v2 = quadTransform.TransformPoint(localVertices[2]); // Top-left
        Vector3 v3 = quadTransform.TransformPoint(localVertices[3]); // Top-right

        // Compute Center (midpoint of all vertices)
        center = (v0 + v1 + v2 + v3) / 4f;

        // Basis Vectors 
        // u = v1 - v0; // Width direction
        // v = v2 - v0; // Height direction
        // Transform local basis vectors to world space (helps with non-uniform scaling, rounding errors)
        u = quadTransform.right * quadTransform.localScale.x;  // Width direction
        v = quadTransform.up * quadTransform.localScale.y;    // Height direction

        // Normal Vector 
        normal = Vector3.Cross(u, v).normalized;


        /* Only if necessary
        // Ensure small floating-point errors are removed
        center = new Vector3(Mathf.Round(center.x * 100000f) / 100000f, 
                            Mathf.Round(center.y * 100000f) / 100000f, 
                            Mathf.Round(center.z * 100000f) / 100000f);
        */

        // Output Results
        //Debug.Log($"Center: {center}");
        //Debug.Log($"Normal: {normal}");
        //Debug.Log($"u (Width): {u}");
        //Debug.Log($"v (Height): {v}");
    }



    void OnValidate() {
		if (!materialInitFlag) {
			materialInitFlag = true;
			material.SetDefaultValues();
		}

        ExtractQuadParameters();

        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();

		MeshRenderer renderer = GetComponent<MeshRenderer>();
		if (renderer != null) {
			if (materialObjectID != gameObject.GetInstanceID()) {
				renderer.sharedMaterial = new Material(renderer.sharedMaterial);
				materialObjectID = gameObject.GetInstanceID();
			}
			renderer.sharedMaterial.color = material.color;
		}
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
