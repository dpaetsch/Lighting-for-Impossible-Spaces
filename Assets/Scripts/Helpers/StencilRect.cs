using UnityEngine;

public struct StencilRect {
	public Vector3 center; // Center position of the rectangle
    public Vector3 normal; // Normal vector defining orientation
    public Vector3 u; // First basis vector (width direction)
    public Vector3 v; // Second basis vector (height direction)
   // public RayTracingMaterial material; 
	public int layer;
    public int nextLayer; 
}
