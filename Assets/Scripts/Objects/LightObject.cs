using UnityEngine;

public class LightObject {
    public Vector3 position; // Position of the light in world space (center of mesh)
    public float radius; // maximum extent of object 
    public int layer; 
    public int virtualizedLayer; // see roomObject


    public LightObject(Vector3 position, float radius, int layer) {
        this.position = position;
        this.radius = radius;
        this.layer = layer;
    }



    
}
