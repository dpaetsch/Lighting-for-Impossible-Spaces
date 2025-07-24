using UnityEngine;

public struct MeshInfo {
	public int globalTrianglesStartIndex;
	public int triangleCount;
	public RayTracingMaterial material;
	public Vector3 boundsMin;
	public Vector3 boundsMax;
	public int layer;

	public MeshInfo(int globalTrianglesStartIndex, int triangleCount, RayTracingMaterial material, Bounds bounds, int layer) {
		this.globalTrianglesStartIndex = globalTrianglesStartIndex;
		this.triangleCount = triangleCount;
		this.material = material;
		this.boundsMin = bounds.min;
		this.boundsMax = bounds.max;
		this.layer = layer;
	}
}
