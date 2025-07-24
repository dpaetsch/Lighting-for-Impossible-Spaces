using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct TriangleInfo {
	// Vertices
	public Vector3 v0;
	public Vector3 v1;
	public Vector3 v2;
	// Normals
	public Vector3 n0;
	public Vector3 n1;
	public Vector3 n2;

	public int globalMeshesIndex; // So that we can relocate the original mesh just from the triangle.

	public TriangleInfo(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 n0, Vector3 n1, Vector3 n2, int globalMeshesIndex) {
		this.v0 = v0;
		this.v1 = v1;
		this.v2 = v2;
		this.n0 = n0;
		this.n1 = n1;
		this.n2 = n2;
		this.globalMeshesIndex = globalMeshesIndex;
	}
}
