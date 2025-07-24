using UnityEngine;

[System.Serializable]
public struct MaterialData {

	public Color color;
	public Color emissionColor;
	[Range(0,10)] public float emissionStrength;

	public void SetDefaultValues() {
		color = Color.white;
		emissionColor = Color.white;
		emissionStrength = 0;
	}


	// Equality Methods for deduplication
	public bool Equals(MaterialData other) {
		return ApproximatelyEqual(color, other.color) &&
		       ApproximatelyEqual(emissionColor, other.emissionColor) &&
		       Mathf.Approximately(emissionStrength, other.emissionStrength);
	}

	public override bool Equals(object obj) => obj is MaterialData other && Equals(other);

	public override int GetHashCode() {
		unchecked {
			int hash = 17;
			hash = hash * 31 + HashColor(color);
			hash = hash * 31 + HashColor(emissionColor);
			hash = hash * 31 + Mathf.RoundToInt(emissionStrength * 1000f); // Scale to avoid float precision issues
			return hash;
		}
	}

	private static int HashColor(Color c){
		return Mathf.RoundToInt(c.r * 255) ^ 
			   Mathf.RoundToInt(c.g * 255) << 2 ^
			   Mathf.RoundToInt(c.b * 255) << 4 ^ 
			   Mathf.RoundToInt(c.a * 255) << 6;
	}

	private static bool ApproximatelyEqual(Color a, Color b){
		return Mathf.Approximately(a.r, b.r) &&
		       Mathf.Approximately(a.g, b.g) &&
			   Mathf.Approximately(a.b, b.b) &&
			   Mathf.Approximately(a.a, b.a);
	}
}
