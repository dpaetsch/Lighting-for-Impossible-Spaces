using UnityEngine;

public struct StencilInfo {
	public Vector3 center; // Center position of the rectangle
    public Vector3 normal; // Normal vector defining orientation
    public Vector3 u; // First basis vector (width direction)
    public Vector3 v; // Second basis vector (height direction)
	public int layer;
    public int nextLayer; 
}
