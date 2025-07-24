using UnityEngine;

public struct MeshInfo {
	public int triangleStartIndex;
	public int triangleCount;
	public RayTracingMaterial material;
	public Vector3 boundsMin;
	public Vector3 boundsMax;
	public int layer;

	public MeshInfo(int triangleStartIndex, int triangleCount, RayTracingMaterial material, Bounds bounds, int layer) {
		this.triangleStartIndex = triangleStartIndex;
		this.triangleCount = triangleCount;
		this.material = material;
		this.boundsMin = bounds.min;
		this.boundsMax = bounds.max;
		this.layer = layer;
	}
}
