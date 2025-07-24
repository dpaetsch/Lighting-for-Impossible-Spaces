using UnityEngine;

public class RayTracedMesh : MonoBehaviour {
	[Header("Settings")]
	public RayTracingMaterial[] materials;

	[Header("Info")]
	public MeshRenderer meshRenderer;
	public MeshFilter meshFilter;
	public int triangleCount;

	[SerializeField, HideInInspector] int materialObjectID;
	[SerializeField] Mesh mesh;
	[SerializeField] MeshChunk[] localChunks;
	MeshChunk[] worldChunks;

	[Header("Stencil Buffer Info")]
	public int layer = 1;

	// Determines if the object is a stencil buffer
	public bool IsStencilBuffer = false;

	// Determines what is the next layer (given that it is a stencil buffer)
	public int nextLayerIfBuffer = 2; 
	// This makes the next layer visible through the stencil buffer on the next layer.



	public MeshChunk[] GetSubMeshes() {
        //Debug.Log("GetSubMeshes START");
		if (mesh.triangles.Length / 3 > RayTracingManager.TriangleLimit) {
			throw new System.Exception($"Please use a mesh with fewer than {RayTracingManager.TriangleLimit} triangles");
		}


		// Split mesh into chunks (if result is not already cached)
        //Debug.Log("meshFilter: " + meshFilter);
        //Debug.Log("mesh: " + mesh);
        //Debug.Log("localChunks: " + localChunks.Length);
        //Debug.Log(mesh != meshFilter.sharedMesh);
		if (meshFilter != null && (mesh != meshFilter.sharedMesh || localChunks == null)) {
            //Debug.Log("Not Cached, null");
			mesh = meshFilter.sharedMesh;
            //Debug.Log("mesh: " + mesh);
			localChunks = MeshSplitter.CreateChunks(mesh, layer, IsStencilBuffer, nextLayerIfBuffer);
		} else {
            //Debug.Log("Cached");
        }
		

		//Debug.Log("DOING MY THING");
		mesh = meshFilter.sharedMesh;
		localChunks = MeshSplitter.CreateChunks(mesh, layer, IsStencilBuffer, nextLayerIfBuffer);


        //Debug.Log("worldChunks: " + worldChunks);
        
		if (worldChunks == null || worldChunks.Length != localChunks.Length) {
            //Debug.Log("New World Chunks");
            //Debug.Log("localChunks: " + localChunks.Length);
			worldChunks = new MeshChunk[localChunks.Length];
		}

		// Transform to world space
		// TODO: upload matrices to gpu to avoid having to contantly upload all mesh data
		Vector3 pos = transform.position;
		Quaternion rot = transform.rotation;
		Vector3 scale = transform.lossyScale;

        //Debug.Log("worldChunks: " + worldChunks.Length);
		for (int i = 0; i < worldChunks.Length; i++) {
			MeshChunk localChunk = localChunks[i];

			if (worldChunks[i] == null || worldChunks[i].triangles.Length != localChunk.triangles.Length) {
				worldChunks[i] = new MeshChunk(new Triangle[localChunk.triangles.Length], localChunk.bounds, localChunk.subMeshIndex);
			}
			UpdateWorldChunkFromLocal(worldChunks[i], localChunk, pos, rot, scale);
		}

        //Debug.Log("GetSubMeshes Done");
        //Debug.Log("worldChunks: " + worldChunks.Length);
		return worldChunks;
	} 

	void UpdateWorldChunkFromLocal(MeshChunk worldChunk, MeshChunk localChunk, Vector3 pos, Quaternion rot, Vector3 scale) {
        //Debug.Log("UpdateWorldChunkFromLocal");

		Triangle[] localTris = localChunk.triangles;

		Vector3 boundsMin = PointLocalToWorld(localTris[0].posA, pos, rot, scale);
		Vector3 boundsMax = boundsMin;

		for (int i = 0; i < localTris.Length; i++)
		{
			Vector3 worldA = PointLocalToWorld(localTris[i].posA, pos, rot, scale);
			Vector3 worldB = PointLocalToWorld(localTris[i].posB, pos, rot, scale);
			Vector3 worldC = PointLocalToWorld(localTris[i].posC, pos, rot, scale);
			Vector3 worldNormA = DirectionLocalToWorld(localTris[i].normalA, rot);
			Vector3 worldNormB = DirectionLocalToWorld(localTris[i].normalB, rot);
			Vector3 worldNormC = DirectionLocalToWorld(localTris[i].normalC, rot);
			Triangle worldTri = new Triangle(worldA, worldB, worldC, worldNormA, worldNormB, worldNormC, layer, IsStencilBuffer ? 1 : 0, nextLayerIfBuffer);
			worldChunk.triangles[i] = worldTri;

			boundsMin = Vector3.Min(boundsMin, worldA);
			boundsMax = Vector3.Max(boundsMax, worldA);
			boundsMin = Vector3.Min(boundsMin, worldB);
			boundsMax = Vector3.Max(boundsMax, worldB);
			boundsMin = Vector3.Min(boundsMin, worldC);
			boundsMax = Vector3.Max(boundsMax, worldC);
		}

		worldChunk.bounds = new Bounds((boundsMin + boundsMax) / 2, boundsMax - boundsMin);
		worldChunk.subMeshIndex = localChunk.subMeshIndex;
	}

	static Vector3 PointLocalToWorld(Vector3 p, Vector3 pos, Quaternion rot, Vector3 scale) {
		return rot * Vector3.Scale(p, scale) + pos;
	}

	static Vector3 DirectionLocalToWorld(Vector3 dir, Quaternion rot) {
		return rot * dir;
	}

	public RayTracingMaterial GetMaterial(int subMeshIndex) {
		return materials[Mathf.Min(subMeshIndex, materials.Length - 1)];
	}

	void OnValidate(){
		if (materials == null || materials.Length == 0) {
			materials = new RayTracingMaterial[1];
			materials[0].SetDefaultValues();
		}

		if (meshRenderer == null || meshFilter == null) {
			meshRenderer = GetComponent<MeshRenderer>();
			meshFilter = GetComponent<MeshFilter>();
		}


		SetUpMaterialDisplay();
		triangleCount = meshFilter.sharedMesh.triangles.Length / 3;
	}

	void SetUpMaterialDisplay() {
		if (gameObject.GetInstanceID() != materialObjectID) {
			materialObjectID = gameObject.GetInstanceID();
			Material[] originalMaterials = meshRenderer.sharedMaterials;
			Material[] newMaterials = new Material[originalMaterials.Length];
			Shader shader = Shader.Find("Standard");
			for (int i = 0; i < meshRenderer.sharedMaterials.Length; i++) {
				newMaterials[i] = new Material(shader);
			}
			meshRenderer.sharedMaterials = newMaterials;
		}

		for (int i = 0; i < meshRenderer.sharedMaterials.Length; i++) {
			RayTracingMaterial mat = materials[Mathf.Min(i, materials.Length - 1)];
			bool displayEmissiveCol = mat.color.maxColorComponent < mat.emissionColor.maxColorComponent * mat.emissionStrength;
			Color displayCol = displayEmissiveCol ? mat.emissionColor * mat.emissionStrength : mat.color;
			meshRenderer.sharedMaterials[i].color = displayCol;
		}
	}
}
