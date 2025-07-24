using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct TriangleInfo {
	public Vector3 v0;
	public Vector3 v1;
	public Vector3 v2;

	public Vector3 normal0;
	public Vector3 normal1;
	public Vector3 normal2;

	public int meshIndex;

	public TriangleInfo(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal0, Vector3 normal1, Vector3 normal2, int meshIndex) {
		this.v0 = v0;
		this.v1 = v1;
		this.v2 = v2;
		this.normal0 = normal0;
		this.normal1 = normal1;
		this.normal2 = normal2;
		this.meshIndex = meshIndex;
	}
}