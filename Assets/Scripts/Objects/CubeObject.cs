using UnityEngine;

[ExecuteAlways]
public class CubeObject : MonoBehaviour {
    [Header("Material & Layer")]

    public int layer = 0;
    public int virtualizedLayer = 0;

    public MaterialData material;
    public bool isLightSource;
    [ReadOnly] public int materialID;

    [ReadOnly] public Vector3 center;

    public Matrix4x4 worldMatrix => transform.localToWorldMatrix;
    public Matrix4x4 inverseWorldMatrix => transform.worldToLocalMatrix;

    [ReadOnly] public bool isDirty = true;


    private Vector3 prevPosition;
    private Quaternion prevRotation;
    private Vector3 prevScale;
    private Vector3 prevCenter;

    void Start(){
        UpdateCubeData();
    }

    void OnValidate(){
        isDirty = true;
        center = PointLocalToWorld(Vector3.zero);
        UpdateCubeData();
        UpdateMaterial();
    }

    void Update() {
        center = PointLocalToWorld(Vector3.zero);
        if (transform.position != prevPosition || transform.rotation != prevRotation || transform.lossyScale != prevScale || center != prevCenter) {
            isDirty = true;
            UpdateCubeData();
        }
    }

    public void UpdateCubeData() {
        prevPosition = transform.position;
        prevRotation = transform.rotation;
        prevScale = transform.lossyScale;
        prevCenter = center;
        
        isDirty = false;
    }

    Vector3 PointLocalToWorld(Vector3 p) {
        return transform.rotation * Vector3.Scale(p, transform.lossyScale) + transform.position;
    }

    public float GetDistanceFromCenterToVertex() {
        // Local-space cube corners (unit cube centered at origin)
        Vector3[] localCorners = new Vector3[] {
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f,  0.5f),
            new Vector3(-0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f,  0.5f,  0.5f),
            new Vector3( 0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f,  0.5f, -0.5f),
            new Vector3( 0.5f,  0.5f,  0.5f),
        };

        float maxDistance = 0f;

        foreach (var localCorner in localCorners) {
            // Transform corner to world space
            Vector3 worldCorner = PointLocalToWorld(localCorner);
            float distance = Vector3.Distance(worldCorner, center);
            maxDistance = Mathf.Max(maxDistance, distance);
        }

        return maxDistance;
    }

    void UpdateMaterial() {
        isLightSource = material.emissionStrength > 0f && material.emissionColor.maxColorComponent > 0f;
    }

}
