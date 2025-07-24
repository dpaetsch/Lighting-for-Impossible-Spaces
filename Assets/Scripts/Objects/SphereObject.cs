using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SphereObject : MonoBehaviour {
	public MeshRenderer meshRenderer;

	public RayTracingMaterial material;
	public bool isLightSource;

	[ReadOnly] public Vector3 boundsMax;
	[ReadOnly] public Vector3 boundsMin;

	[Header("Stencil Buffer Info")]
	public int layer = 1;



	void OnValidate() {
		
		updateMaterial();
		isLightSource = material.emissionStrength > 0f && material.emissionColor.maxColorComponent > 0f;
	}

	void updateMaterial() {
        if (meshRenderer == null) return;

        Material unityMat = meshRenderer.sharedMaterial;
        if (unityMat == null) return;

        // Copy base color
        if (unityMat.HasProperty("_Color")) {
            material.color = unityMat.color;
        } 
    }

	public float getRadius(){
		// Get the radius of the sphere
		Vector3 scale = transform.lossyScale;
		float radius = scale.x * 0.5f;
		return radius;
	}

	public void calculateBounds() {
		Vector3 pos = transform.position;
		float radius = transform.lossyScale.x * 0.5f;

		boundsMin = pos - Vector3.one * radius;
		boundsMax = pos + Vector3.one * radius;
	}

}
 