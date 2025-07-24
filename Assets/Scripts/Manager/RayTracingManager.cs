using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Mathf;


[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    public const int TriangleLimit = 1500;

    [SerializeField] bool useShaderInSceneView;

    [SerializeField] bool useSimpleShape; // Without raytracing, just getting the shapes of objects

    [SerializeField] Shader rayTracingShader;

    [SerializeField, Range(0, 32)] int maxBounceCount = 4;
    [SerializeField, Range(0, 256)] int numRaysPerPixel = 2;


    [Header("Info")]
    
    [SerializeField] int numMeshInfo;
	[SerializeField] int numMeshChunks;
	[SerializeField] int numTriangles;
    [SerializeField] int numStencils;

    List<Triangle> allTriangles;
	List<MeshInfo> allMeshInfo;
	

    [Header("Environment Settings")]

    [SerializeField] EnvironmentSettings environmentSettings;



    [Header("Stencil Buffer Info")]
    [SerializeField] int currentLayer = 1;


    Material rayTracingMaterial;


    // --- Buffers ---
	ComputeBuffer sphereBuffer;
    ComputeBuffer triangleBuffer;
	ComputeBuffer meshInfoBuffer;
    ComputeBuffer stencilBuffer;




    // Called after each camera (e.g. game or scene camera) has finished rendering into the src texture.
    void OnRenderImage(RenderTexture src, RenderTexture target){
        if(Camera.current.name != "SceneCamera" || useShaderInSceneView){
            InitFrame();
            // Set up a material using the ray tracing shader
            ShaderHelper.InitMaterial(rayTracingShader, ref rayTracingMaterial);
            UpdateCameraParams(Camera.current);

            // Run the ray tracing shader and draw the result to the screen
            Graphics.Blit(null, target, rayTracingMaterial);
        } else {
            InitFrame();
            Graphics.Blit(src, target);
        } 
    }

    void UpdateCameraParams(Camera cam){
        float planeHeight = cam.nearClipPlane * Tan(cam.fieldOfView * 0.5f * Deg2Rad) * 2;
        float planeWidth = planeHeight * cam.aspect;
        // Send data to shader
        rayTracingMaterial.SetVector("ViewParams", new Vector3(planeWidth, planeHeight, cam.nearClipPlane));
        rayTracingMaterial.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);    
    }

    void InitFrame(){
        // Create materials used in blits
		ShaderHelper.InitMaterial(rayTracingShader, ref rayTracingMaterial);

        // Update Data
        UpdateCameraParams(Camera.current);
        CreateSpheres();
        CreateMeshes();
        CreateStencilRects();
        SetShaderParams();
    }

    // For Spheres
    void CreateSpheres(){
        // Create sphere data from the sphere objects in the scene
        RayTracedSphere[] sphereObjects = FindObjectsOfType<RayTracedSphere>();
        Sphere[] spheres = new Sphere[sphereObjects.Length];

        for (int i = 0; i < sphereObjects.Length; i++) {
            spheres[i] = new Sphere() {
                position = sphereObjects[i].transform.position,
                radius = sphereObjects[i].transform.localScale.x * 0.5f,
                material = sphereObjects[i].material,
                layer = sphereObjects[i].layer,
            };

        }
        //Debug.Log("DOING SPHERES");
        //Debug.Log("NumSpheres: " + sphereObjects.Length);

        // Create buffer containing all sphere data, and send it to the shader
        ShaderHelper.CreateStructuredBuffer(ref sphereBuffer, spheres);
        rayTracingMaterial.SetBuffer("Spheres", sphereBuffer);
        rayTracingMaterial.SetInt("NumSpheres", sphereObjects.Length);
    }

    // For Meshes
    void CreateMeshes() {
		RayTracedMesh[] meshObjects = FindObjectsOfType<RayTracedMesh>();

        Debug.Log("NUMBER OF OBJECTS: " + meshObjects.Length);

		allTriangles ??= new List<Triangle>();
		allMeshInfo ??= new List<MeshInfo>();
        allTriangles.Clear();
		allMeshInfo.Clear();


		for (int i = 0; i < meshObjects.Length; i++) {
			MeshChunk[] chunks = meshObjects[i].GetSubMeshes();

            Debug.Log("NUMBER OF CHUNKS: " + chunks.Length);

			foreach (MeshChunk chunk in chunks) {
                //Debug.Log("CHUNK: " + chunk.triangles.Length);
				RayTracingMaterial material = meshObjects[i].GetMaterial(chunk.subMeshIndex);
				allMeshInfo.Add(new MeshInfo(allTriangles.Count, chunk.triangles.Length, material, chunk.bounds));
				allTriangles.AddRange(chunk.triangles);
			}
		}

        numMeshChunks = allMeshInfo.Count;
		numTriangles = allTriangles.Count;

		ShaderHelper.CreateStructuredBuffer(ref triangleBuffer, allTriangles);
		ShaderHelper.CreateStructuredBuffer(ref meshInfoBuffer, allMeshInfo);

        numMeshInfo = allMeshInfo.Count;

		rayTracingMaterial.SetBuffer("Triangles", triangleBuffer);
		rayTracingMaterial.SetBuffer("AllMeshInfo", meshInfoBuffer);
		rayTracingMaterial.SetInt("NumMeshes", allMeshInfo.Count);
	}

    // For Stencil Buffers
    void CreateStencilRects() {
        // Find all StencilWindow objects in the scene
        StencilObject[] stencilObjects = FindObjectsOfType<StencilObject>();
        StencilRect[] stencilRects = new StencilRect[stencilObjects.Length];

        for (int i = 0; i < stencilObjects.Length; i++) {
            stencilObjects[i].ExtractQuadParameters();
            stencilRects[i] = new StencilRect() {
                center = stencilObjects[i].GetCenter(),
                normal = stencilObjects[i].GetNormal(),
                u = stencilObjects[i].GetU(),
                v= stencilObjects[i].GetV(),
                //material = stencilObjects[i].material,
                layer = stencilObjects[i].layer,
                nextLayer = stencilObjects[i].nextLayer,
            };
        }

        // Create buffer containing all stencil rectangle data, and send it to the shader
        ShaderHelper.CreateStructuredBuffer(ref stencilBuffer, stencilRects);
        rayTracingMaterial.SetBuffer("StencilRects", stencilBuffer);
        rayTracingMaterial.SetInt("NumStencilRects", stencilObjects.Length);
	}


        

    void SetShaderParams() {
		rayTracingMaterial.SetInt("MaxBounceCount", maxBounceCount);
		rayTracingMaterial.SetInt("NumRaysPerPixel", numRaysPerPixel);

        rayTracingMaterial.SetInt("UseSimpleShape", useSimpleShape ? 1 : 0);

        rayTracingMaterial.SetInteger("EnvironmentEnabled", environmentSettings.enabled ? 1 : 0);
		rayTracingMaterial.SetColor("GroundColour", environmentSettings.groundColour);
		rayTracingMaterial.SetColor("SkyColourHorizon", environmentSettings.skyColourHorizon);
		rayTracingMaterial.SetColor("SkyColourZenith", environmentSettings.skyColourZenith);
		rayTracingMaterial.SetFloat("SunFocus", environmentSettings.sunFocus);
		rayTracingMaterial.SetFloat("SunIntensity", environmentSettings.sunIntensity);

        rayTracingMaterial.SetInt("currentLayer", currentLayer);
	}



    // Called when the script instance is being loaded
    void OnDisable() {
        ShaderHelper.Release(triangleBuffer, meshInfoBuffer);
        ShaderHelper.Release(sphereBuffer);
        ShaderHelper.Release(stencilBuffer);
	}

    // Called when the script is loaded or a value is changed in the inspector (Called in the editor only)
    void OnValidate(){
		maxBounceCount = Mathf.Max(0, maxBounceCount);
		numRaysPerPixel = Mathf.Max(1, numRaysPerPixel);
		environmentSettings.sunFocus = Mathf.Max(1, environmentSettings.sunFocus);
		environmentSettings.sunIntensity = Mathf.Max(0, environmentSettings.sunIntensity);

	}




}
