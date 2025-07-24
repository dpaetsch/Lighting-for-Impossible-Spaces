using UnityEngine;

public struct CubeInfo {
    public Vector3 center; // Center of the cube in world space
    public Matrix4x4 worldMatrix; // World matrix of the cube
    public Matrix4x4 inverseWorldMatrix; // Inverse world matrix of the cube
    public int materialID;
    public int layer;
}
