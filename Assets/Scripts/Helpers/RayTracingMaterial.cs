using UnityEngine;

[System.Serializable]
public struct RayTracingMaterial
{
	public enum MaterialFlag {
		None,
		CheckerPattern,
		InvisibleLight
	}

	public Color color;
	public Color emissionColor;
	public Color specularColor;
	[Range(0,10)] public float emissionStrength;
	[Range(0, 1)] public float smoothness;
	[Range(0, 1)] public float specularProbability;
	public MaterialFlag flag;

	public void SetDefaultValues() {
		color = Color.white;
		emissionColor = Color.white;
		emissionStrength = 0;
		specularColor = Color.white;
		smoothness = 0;
		specularProbability = 1;
	}
}