Shader "Custom/RayTracing" {
    SubShader {
        Cull Off ZWrite Off ZTest Always

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex); 
                o.uv = v.uv;
                return o;
            }

            // --- Settings and constants ---
			static const float PI = 3.1415;

			int cameraLayer; // Layer of the camera
			int nextLayer; // Layer to display, that we are about to walk into

            // RayTracing Settings
			int NumRaysPerPixel;
			int MaxBounceCount;
			int maxPropagationDepth;

			// View options
            int UseSimpleShape;
			int useRayTracing;
			int useImportanceSampling;

			// BVH Settings
			int useBVH;
			int useFullObjectsInBVH; // if true, BVH wrapper nodes will contain full objects (meshes and spherers) instead of individual triangles and spheres
			int showBVHDepth;
			int bvhDepth;
			int bvhMaxDepth;
			int accumulateBVHColors;

			// Debug Info
			int ShowIntersectionCount;
			int maxIntersectionTests; // Maximum number of intersection tests for all traces
			int numIntersectionTests; // keeps track of number of bounces for each trace (not imported)

            // Environment Light
            int useEnvironmentLight;
            float3 GroundColor;
            float3 SkyColorHorizon;
            float3 SkyColorZenith;
            float SunIntensity;
            float SunFocus;

			// Ambient Light
			int useAmbientLight;
			float3 AmbientLightColor;
			float AmbientLightIntensity;

			// View Parameters
            float3 ViewParams;
			float4x4 CamLocalToWorldMatrix;


            // --- Structures ---

            struct RayTracingMaterial {
				float4 color;
				float4 emissionColor;
				float emissionStrength;
			};

			struct Ray {
				float3 origin;
				float3 dir;
				float3 invDir; // Inverse direction for faster calculations
			};

			struct HitInfo {
				bool didHit;
				float dst;
				float3 hitPoint;
				float3 normal;
				RayTracingMaterial material;
				int nextLayerIfBuffer;
				int layerOfHit;
			};

			// --- Info Structs ---

			struct MeshInfo {
				int firstTriangleIndex;
				int numTriangles;
				RayTracingMaterial material;
				float3 boundsMin;
				float3 boundsMax;
				int layer;
			};

			struct TriangleInfo {
				float3 v0;
				float3 v1;
				float3 v2;
				float3 normal0;
				float3 normal1;
				float3 normal2;
				int meshIndex;
			};

            struct SphereInfo {
				float3 position;
				float radius;
				RayTracingMaterial material;
				int layer;
			};

			 struct StencilInfo {
				float3 center; // Center position of the rectangle
				float3 normal; // Normal vector defining orientation
				float3 u; // First basis vector (width direction)
				float3 v; // Second basis vector (height direction)
				int layer;
				int nextLayer; 
			};

			struct RoomInfo {
				int layer;
				int meshIndex;
				int numMeshes;
				int spheresIndex;    
				int numSpheres;
				int stencilIndex;
				int numStencils;
				int numWrappers;
				int wrappersIndex;
				int numbvhNodes;
				int bvhNodesIndex;
			};

			struct LightInfo {
				float3 position;
				float radius;
				int layer;
			};

			struct BVHNodeInfo {
				float3 minBounds;
				float3 maxBounds;
				int isLeaf; 
				int startWrapperIndex;
				int lengthOfWrappers;
				int leftChildIndex;
				int rightChildIndex;
			};

			struct WrapperInfo {
				float3 minBounds; // minimum corner of the bounding box
				float3 maxBounds; // maximum corner of the bounding box
				int isTriangle; // true if this node contains a triangle, false if it contains a sphere 
				int meshIndex; // index of the mesh this triangle belongs to, or -1 if it's a sphere (relative to room)
				int index; // index of triangle (in mesh) or sphere (in all spheres list) (relative to room)
			};



            // --- Buffers ---
			StructuredBuffer<MeshInfo> MeshInfos;
            StructuredBuffer<TriangleInfo> TriangleInfos;
			StructuredBuffer<SphereInfo> SphereInfos;
			StructuredBuffer<StencilInfo> StencilInfos;
			StructuredBuffer<RoomInfo> RoomInfos;
			StructuredBuffer<LightInfo> LightInfos;
			StructuredBuffer<BVHNodeInfo> BvhNodeInfos;
			StructuredBuffer<WrapperInfo> WrapperInfos;
			

			int NumMeshes;
			int NumTriangles;
			int NumSpheres;
			int NumStencilInfos;
			int NumRooms;
			int NumLights;

			int NumBVHNodes;
			int NumWrappers;
			// ---------------- 




            // --- Random Number Generator ----
            uint NextRandom(inout uint state) {
				state = state * 747796405 + 2891336453;
				uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
				result = (result >> 22) ^ result;
				return result;
			}

			float RandomValue(inout uint state) {
				return NextRandom(state) / 4294967295.0; // 2^32 - 1
			}

			// Random value in normal distribution (with mean=0 and sd=1)
			float RandomValueNormalDistribution(inout uint state) {
				// Thanks to https://stackoverflow.com/a/6178290
				float theta = 2 * 3.1415926 * RandomValue(state);
				float rho = sqrt(-2 * log(RandomValue(state)));
				return rho * cos(theta);
			}

			// Calculate a random direction
			float3 RandomDirection(inout uint state) {
				// Thanks to https://math.stackexchange.com/a/1585996
				float x = RandomValueNormalDistribution(state);
				float y = RandomValueNormalDistribution(state);
				float z = RandomValueNormalDistribution(state);
				return normalize(float3(x, y, z));
			}

			float2 RandomPointInCircle(inout uint rngState) {
				float angle = RandomValue(rngState) * 2 * PI;
				float2 pointOnCircle = float2(cos(angle), sin(angle));
				return pointOnCircle * sqrt(RandomValue(rngState));
			}

			float2 mod2(float2 x, float2 y) {
				return x - y * floor(x/y);
			}

            // Random direction in the hemisphere oriented around the given normal verctor
            float3 RandomHemisphereDirection(float3 normal,  inout uint rngState) {
                float3 dir = RandomDirection(rngState);
                return dir*sign(dot(dir, normal));
            }

			// --- Ray to light source Function ---
			float3 GetRayToLightSource(float3 lightPos, float3 hitPoint) {
				float3 rayToLight = lightPos - hitPoint;
				return normalize(rayToLight);
			}


			// --- Environment light Function ---
            // Crude sky color function for background light
			float3 GetEnvironmentLight(Ray ray) {
				if (!useEnvironmentLight) { return 0; }

				float skyGradientT = pow(smoothstep(0, 0.4, ray.dir.y), 0.35);
				float groundToSkyT = smoothstep(-0.01, 0, ray.dir.y);
				float3 skyGradient = lerp(SkyColorHorizon, SkyColorZenith, skyGradientT);

				float sun = pow(max(0, dot(ray.dir, _WorldSpaceLightPos0.xyz)), SunFocus) * SunIntensity;
				// Combine ground, sky, and sun
				float3 composite = lerp(GroundColor, skyGradient, groundToSkyT) + sun * (groundToSkyT>=1);
				return composite;
			}

			// --- Ambient Light Function ---
			float3 GetAmbientLight(Ray ray){
				if(!useAmbientLight) { return 0; }
				float3 ambientLight = AmbientLightColor * AmbientLightIntensity;
				return ambientLight;
			}

			// --- Hash Color Function ---
			float3 hashColor(int id) {
				// Simple hash function to generate pseudo-random color from ID
				float r = frac(sin(id * 12.9898) * 43758.5453);
				float g = frac(sin(id * 78.233) * 12345.6789);
				float b = frac(sin(id * 45.164) * 98765.4321);
				return float3(r, g, b);
			}

			// HSV to RGB conversion
			float3 hsv2rgb(float h, float s, float v) {
				float3 c = float3(h, s, v);
				float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
				float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
				return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
			}

			// Deterministic rainbow color based on ID
			float3 rainbowColor(int id) {
				float hue = frac(id * 0.15); // Increase for more spacing
				return hsv2rgb(hue, 1.0, 1.0); // Full saturation, full brightness
			}

			// Unique Red-to-Blue Color Gradient
			float3 redToBlueColor(int id, int maxId) {
				float t = clamp((float)id / (float)maxId, 0.0, 1.0);
				float r = lerp(1.0, 0.0, t); // Red to 0
				float g = 0.0;
				float b = lerp(0.0, 1.0, t); // Blue to 1
				return float3(r, g, b);
			}




            // --- Ray Intersection Functions ---
		
			// Calculate the intersection of a ray with a sphere
			HitInfo RaySphere(Ray ray, SphereInfo sphere) {
				numIntersectionTests++;
				float3 sphereCentre = sphere.position;
				float sphereRadius = sphere.radius;

				HitInfo hitInfo = (HitInfo)0;
				float3 offsetRayOrigin = ray.origin - sphereCentre;
				// From the equation: sqrLength(rayOrigin + rayDir * dst) = radius^2
				// Solving for dst results in a quadratic equation with coefficients:
				float a = dot(ray.dir, ray.dir); // a = 1 (assuming unit vector)
				float b = 2 * dot(offsetRayOrigin, ray.dir);
				float c = dot(offsetRayOrigin, offsetRayOrigin) - sphereRadius * sphereRadius;
				// Quadratic discriminant
				float discriminant = b * b - 4 * a * c; 

				// No solution when d < 0 (ray misses sphere)
				if (discriminant >= 0) {
					// Distance to nearest intersection point (from quadratic formula)
					float dst = (-b - sqrt(discriminant)) / (2 * a);

					// Ignore intersections that occur behind the ray
					if (dst >= 0) {
						hitInfo.didHit = true;
						hitInfo.dst = dst;
						hitInfo.hitPoint = ray.origin + ray.dir * dst;
						hitInfo.normal = normalize(hitInfo.hitPoint - sphereCentre);
						hitInfo.material = sphere.material;
						hitInfo.layerOfHit = sphere.layer;
					}
				}
				return hitInfo;
			}

            // Calculate the intersection of a ray with a triangle using Möller–Trumbore algorithm
			HitInfo RayTriangle(Ray ray, TriangleInfo tri) {
				numIntersectionTests++;
				float3 edgeAB = tri.v1 - tri.v0;
				float3 edgeAC = tri.v2 - tri.v0;
				float3 normalVector = cross(edgeAB, edgeAC);
				float3 ao = ray.origin - tri.v0;
				float3 dao = cross(ao, ray.dir);

				float determinant = -dot(ray.dir, normalVector);
				float invDet = 1 / determinant;
				
				// Calculate dst to triangle & barycentric coordinates of intersection point
				float dst = dot(ao, normalVector) * invDet;
				float u = dot(edgeAC, dao) * invDet;
				float v = -dot(edgeAB, dao) * invDet;
				float w = 1 - u - v;
				
				// Initialize hit info
				HitInfo hitInfo;
				hitInfo.didHit = determinant >= 1E-6 && dst >= 0 && u >= 0 && v >= 0 && w >= 0;
				hitInfo.hitPoint = ray.origin + ray.dir * dst;
				hitInfo.normal = normalize(tri.normal0 * w + tri.normal1 * u + tri.normal2 * v);
				hitInfo.dst = dst;
				//hitInfo.layerOfHit = tri.layer;
				return hitInfo;
			}

			// Thanks to https://gist.github.com/DomNomNom/46bb1ce47f68d255fd5d
			bool RayBoundingBox(Ray ray, float3 boxMin, float3 boxMax) {
				numIntersectionTests++;
				float3 invDir = ray.invDir;
				float3 tMin = (boxMin - ray.origin) * invDir;
				float3 tMax = (boxMax - ray.origin) * invDir;
				float3 t1 = min(tMin, tMax);
				float3 t2 = max(tMin, tMax);
				float tNear = max(max(t1.x, t1.y), t1.z);
				float tFar = min(min(t2.x, t2.y), t2.z);
				return tNear <= tFar && tFar >= 0; // Return true if the ray intersects the bounding box
			};

			// Thanks to https://tavianator.com/2011/ray_box.html
			float RayBoundingBoxDst(Ray ray, float3 boxMin, float3 boxMax) {
				numIntersectionTests++;
				float3 invDir = ray.invDir;
				// float3 tMin = (boxMin - ray.origin) * ray.invDir;
				// float3 tMax = (boxMax - ray.origin) * ray.invDir;
				float3 tMin = (boxMin - ray.origin) * invDir;
				float3 tMax = (boxMax - ray.origin) * invDir;
				float3 t1 = min(tMin, tMax);
				float3 t2 = max(tMin, tMax);
				float tNear = max(max(t1.x, t1.y), t1.z);
				float tFar = min(min(t2.x, t2.y), t2.z);

				bool hit = tFar >= tNear && tFar > 0;
				float dst = hit ? tNear > 0 ? tNear : 0 : 1.#INF;
				return dst;
			};


			HitInfo RayRectangle(Ray ray, StencilInfo rect) {
				numIntersectionTests++;
				float3 center = rect.center;
				float3 normal = rect.normal;
				float3 u = rect.u;
				float3 v = rect.v;
 
				HitInfo hitInfo = (HitInfo)0;

				float width = length(u);
				float height = length(v);
				
				// Compute denominator to check if ray and plane are parallel
				float denom = dot(ray.dir, normal);
				if (abs(denom) < 1e-6) return hitInfo; // No intersection if parallel

				// Compute distance along ray to plane intersection
				float t = dot(center - ray.origin, normal) / denom; // Computers how far along the ray t it intersects the plane of the rectangle.
				if (t < 0) return hitInfo; // Intersection behind the ray

				// Compute intersection point of ray and plane
				float3 hitPoint = ray.origin + ray.dir * t;

				// Convert hitPoint to local rectangle space
				float3 localHit = hitPoint - center;

				// Compute projected coordinates along u and v basis vectors
				// float uProj = dot(localHit, u);
				// float vProj = dot(localHit, v);
				float uProj = dot(localHit, normalize(u)); 
				float vProj = dot(localHit, normalize(v));

				// Check if inside rectangle bounds
				if (abs(uProj) <= width * 0.5 && abs(vProj) <= height * 0.5) {
					hitInfo.didHit = true;
					hitInfo.dst = t;
					hitInfo.hitPoint = hitPoint;
					hitInfo.normal = normal;
				}

				return hitInfo;
			}


			// --- Closest Hit Calculation (Helpers) ---
			HitInfo closestMeshHit(Ray ray, int currentLayer){
				if(NumMeshes == 0) return (HitInfo)0; // No meshes to check			
				HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF; // We haven't hit anything yet, so 'closest' hit is infinitely far away

				// Raycast against all meshes in the current layer and keep info about the closest hit
				int layerIndex = currentLayer;
				int numMeshes = RoomInfos[layerIndex].numMeshes;
				int firstMeshIndex = RoomInfos[layerIndex].meshIndex;

				for(int meshIndex = firstMeshIndex; meshIndex < firstMeshIndex + numMeshes; meshIndex++) {
					MeshInfo meshInfo = MeshInfos[meshIndex];
					
					// Skip the mesh if ray doesn't intersect its bounding box.
					if (!RayBoundingBox(ray, meshInfo.boundsMin, meshInfo.boundsMax)) { continue; }

					for (int triIndex = meshInfo.firstTriangleIndex; triIndex < meshInfo.numTriangles + meshInfo.firstTriangleIndex; triIndex++) {
						TriangleInfo tri = TriangleInfos[triIndex];
						HitInfo hitInfo = RayTriangle(ray, tri);
						if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
							closestHit = hitInfo; // Captures the closest hit location, material, and layer
							closestHit.material = meshInfo.material;
							closestHit.layerOfHit = meshInfo.layer;
						}
					}
				}
				return closestHit;
			}

			HitInfo closestSphereHit(Ray ray, int currentLayer){ 
				if(NumSpheres == 0) return (HitInfo)0; // No spheres to check
				HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF;

				//Raycast against all spheres in the current layer and keep info about the closest hit
				int layerIndex = currentLayer;
				int numSpheres = RoomInfos[layerIndex].numSpheres;
				int firstSphereIndex = RoomInfos[layerIndex].spheresIndex;

				for(int sphereIndex = firstSphereIndex; sphereIndex < firstSphereIndex + numSpheres; sphereIndex++) {
					SphereInfo sphere = SphereInfos[sphereIndex];
					HitInfo hitInfo = RaySphere(ray, sphere);
					// Check if there is something in the current layer that is closer than the buffer (if it's not lighting)
					if(hitInfo.didHit && hitInfo.dst < closestHit.dst) {
						closestHit = hitInfo; // Captures the closest hit location, material, and layer
					}
				}
				return closestHit;
			}

			// Check if there is a buffer, in the path of the ray, in the current layer 
			// current layer is the layer the ray is in, previous layer is the layer we came from (so that we don't accidentally go back)
			HitInfo checkClosestBuffer(Ray ray, int currentLayer, int previousLayer){
				// Raycast against all rectangles in the current layer and keep info about the closest hit
				HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF;  // We haven't hit anything yet, so 'closest' hit is infinitely far away

				int layerIndex = currentLayer;
				int numStencils = RoomInfos[layerIndex].numStencils;
				int firstStencilIndex = RoomInfos[layerIndex].stencilIndex;

				for(int stencilIndex = firstStencilIndex; stencilIndex < firstStencilIndex + numStencils; stencilIndex++) {
					StencilInfo rect = StencilInfos[stencilIndex];

					// We ignore any buffer than brings us back to the previous layer.
					if(rect.nextLayer == previousLayer) { continue; }

					HitInfo hitInfo = RayRectangle(ray, rect);
					if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
						closestHit = hitInfo;
						closestHit.nextLayerIfBuffer = rect.nextLayer; // Record the next layer if buffer is hit
					}

				}
				return closestHit;
			}

			HitInfo closestBVHHit(Ray ray, int currentLayer) { 
				if(NumBVHNodes == 0) return (HitInfo)0; // No BVH to check
				HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF; // We haven't hit anything yet, so 'closest' hit is infinitely far away


				int layerIndex = currentLayer;


				int stack[16]; // Stack to hold BVH node indices (fixed size) 
				int stackIndex = 0;
				stack[stackIndex++] = RoomInfos[layerIndex].bvhNodesIndex; // Start with the root node index of the room
				
				// Traverse the BVH tree iteratively
				while(stackIndex > 0){
					BVHNodeInfo node = BvhNodeInfos[stack[--stackIndex]];

					if(RayBoundingBox(ray, node.minBounds, node.maxBounds)) {
						if(node.isLeaf){ 
							// If it's a leaf, we check the wrappers in this node
							for(int i = 0; i < node.lengthOfWrappers; i++) {
								int wrapperIndex = node.startWrapperIndex + i;
								WrapperInfo wrapper = WrapperInfos[wrapperIndex];

								if(useFullObjectsInBVH) { // Wrapper Objects contain full objects (meshes and spheres)
									if(wrapper.isTriangle) {
										// Check each triangle in mesh
										MeshInfo meshInfo = MeshInfos[wrapper.meshIndex];

										for (int triIndex = meshInfo.firstTriangleIndex; triIndex < meshInfo.numTriangles + meshInfo.firstTriangleIndex; triIndex++) {
											TriangleInfo tri = TriangleInfos[triIndex];
											HitInfo hitInfo = RayTriangle(ray, tri);
											if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
												closestHit = hitInfo; // Captures the closest hit location, material, and layer
												closestHit.material = meshInfo.material;
												closestHit.layerOfHit = meshInfo.layer;
											}
										}
									} else {
										SphereInfo sphere = SphereInfos[wrapper.index];
										HitInfo hitInfo = RaySphere(ray, sphere);
										if(hitInfo.didHit && hitInfo.dst < closestHit.dst) {
											closestHit = hitInfo; // Captures the closest hit location, material, and layer
										}
									}
								} else {  // Wrapper Objects contain individual triangles and spheres
									if(wrapper.isTriangle) {
										TriangleInfo tri = TriangleInfos[wrapper.index];
										HitInfo hitInfo = RayTriangle(ray, tri);
										if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
											closestHit = hitInfo; // Captures the closest hit location, material, and layer
											closestHit.material = MeshInfos[tri.meshIndex].material;
											closestHit.layerOfHit = currentLayer; // The layer of the hit is the current layer
										}
									} else {
										SphereInfo sphere = SphereInfos[wrapper.index];
										HitInfo hitInfo = RaySphere(ray, sphere);
										if(hitInfo.didHit && hitInfo.dst < closestHit.dst) {
											closestHit = hitInfo; // Captures the closest hit location, material, and layer
										}
									}
								}
								
							}
						} else {
							// If it's not a leaf, we push the children onto the stack

							// See which child node to check first
							BVHNodeInfo childA = BvhNodeInfos[node.leftChildIndex];
							BVHNodeInfo childB = BvhNodeInfos[node.rightChildIndex];

							float dstA = RayBoundingBoxDst(ray, childA.minBounds, childA.maxBounds);
							float dstB = RayBoundingBoxDst(ray, childB.minBounds, childB.maxBounds);

							if(closestHit.dst < dstA && closestHit.dst < dstB) continue; // If we already found a closer hit, skip this node
							
							// We want to look at closest child node first, so push it last
							bool isNearestA = dstA <= dstB;
							float dstNear = isNearestA ? dstA : dstB;
							float dstFar = isNearestA ? dstB : dstA;
							int childIndexNear = isNearestA ? node.leftChildIndex : node.rightChildIndex;
							int childIndexFar = isNearestA ? node.rightChildIndex : node.leftChildIndex;

							if (dstFar < closestHit.dst) stack[stackIndex++] = childIndexFar;
							if (dstNear < closestHit.dst) stack[stackIndex++] = childIndexNear;
						}
					}

				}				
				return closestHit; // Return the closest hit found in the BVH
			}


			// --- Iterative Ray Propagation Through Portals ---
			HitInfo IterativeRayPropagationThroughPortals(int startLayer, Ray ray) {
				HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF;
				HitInfo bufferHit = (HitInfo)0;
				bufferHit.didHit = false; // Initialize buffer hit as not hit
				bufferHit.dst = 1.#INF; // Initialize buffer hit to infinitely far away
				HitInfo previousBestHit = (HitInfo)0;
				previousBestHit.dst = 1.#INF; // Initialize previous best hit to infinitely far away


				int currentLayer = startLayer;
				int nextLayer;
				int previousLayer = -1;

				int maxPropagations = maxPropagationDepth; // Maximum number of times we can go through the layers (for a single ray)

				ray.invDir = 1 / ray.dir; // Precompute the inverse direction for faster calculations

				while (maxPropagations-- > 0) {
					
					if(useBVH){
						HitInfo closest = closestBVHHit(ray, currentLayer);
						if(closest.didHit && closest.dst < closestHit.dst) {
							closestHit = closest; // If we hit something in the BVH, we use that
						}
					} else {
						// Check for objects in the current layer
						HitInfo closestHitMesh = closestMeshHit(ray, currentLayer);
						if (closestHitMesh.didHit && closestHitMesh.dst < closestHit.dst) {
							closestHit = closestHitMesh;
						}

						// Check for spheres in the current layer	
						HitInfo closestHitSphere = closestSphereHit(ray, currentLayer);
						if (closestHitSphere.didHit && closestHitSphere.dst < closestHit.dst) {
							closestHit = closestHitSphere;
						}

					}

					// make sure we are not on the first propagation, since we need a buffer hit
					if(maxPropagations + 1  < maxPropagationDepth){
						if(closestHit.dst < bufferHit.dst){ // check if the new hit is closer than the previous buffer hit (to avoid glitches)
							// if yes, then we avoid drawing this new hit, and return the previous hit
							//return previousBestHit; // Return the previous best hit if we hit something closer than the buffer
						}
					}


					// Find the closest buffer hit in this layer
					HitInfo bufferHit = checkClosestBuffer(ray, currentLayer, previousLayer);
					if(!bufferHit.didHit) return closestHit; // No buffer hit, return the closest hit
					if(closestHit.dst < bufferHit.dst) return closestHit; // If we hit an object before the buffer, return the hit

					previousBestHit = closestHit; // Save the previous best hit before we move to the next layer

					// If we hit a buffer, get the next layer after the buffer
					nextLayer = bufferHit.nextLayerIfBuffer;
					// Move to the next layer and continue the search
					previousLayer = currentLayer;
					currentLayer = nextLayer;

				}
				return closestHit; // Return the closest hit found
			}

			// --- Ray Tracing Functions ---
            float3 Trace(Ray ray, inout uint rngState, int currentLayer) {
				float3 incomingLight = 0;
				float3 rayColor = 1;	

				int StartOfLightingLayer = cameraLayer;

				for (int bounceIndex = 0; bounceIndex <= MaxBounceCount; bounceIndex++) {

					HitInfo hitInfo = IterativeRayPropagationThroughPortals(StartOfLightingLayer, ray);

					numIntersectionTests++;

					if (hitInfo.didHit) {
                        ray.origin = hitInfo.hitPoint;

						// This is for future iterations, we need to know what layer we are in to be able to calculate the light
						StartOfLightingLayer = hitInfo.layerOfHit;

                        //ray.dir = RandomHemisphereDirection(hitInfo.normal, rngState);
                        ray.dir = normalize(hitInfo.normal + RandomDirection(rngState));
                        
						RayTracingMaterial material = hitInfo.material;
						float3 emittedLight = material.emissionColor * material.emissionStrength;
                        //float lightStrength = dot(hitInfo.normal,ray.dir); 
                        incomingLight += emittedLight * rayColor;
                        rayColor *= material.color; //* lightStrength * 2;

						if(bounceIndex == 0){
							incomingLight += GetAmbientLight(ray) * rayColor;
						}

					} else {
                        incomingLight += GetEnvironmentLight(ray) * rayColor;
						break;
					}
				}

				return incomingLight;
			}

			float3 RayTrace(Ray ray, inout uint rngState, int currentLayer) {
				float3 totalIncomingLight = 0;
				for (int i = 0; i < NumRaysPerPixel; i++) {
						totalIncomingLight += Trace(ray, rngState, cameraLayer);
				}
				return totalIncomingLight;
			}


			// --- Importance Sampling Function ---
			float3 ImportanceSample(Ray ray, int currentLayer, int nextLayer) {

				float3 incomingLight = 0;
				float3 rayColor = 1;	
				
				// Find the object of the pixel
				// Check current Layer

				HitInfo hitInfo;

				HitInfo hitInfoCurrent = IterativeRayPropagationThroughPortals(currentLayer, ray);
				HitInfo hitInfoNext = IterativeRayPropagationThroughPortals(nextLayer, ray);

				if (hitInfoCurrent.didHit && hitInfoCurrent.dst < hitInfoNext.dst) {
					hitInfo = hitInfoCurrent; // If we hit something in the current layer, use that
				} else if (hitInfoNext.didHit) {
					hitInfo = hitInfoNext; // If we didn't hit anything in the current layer, use the next layer
				} else {
					return incomingLight;
				}
				
				// Get the material of the object
				RayTracingMaterial material = hitInfo.material;
				float3 emittedLight = material.emissionColor * material.emissionStrength;
				incomingLight += emittedLight * rayColor;
				rayColor *= material.color;

				
				// Loop over all lights in the scene and sample them
				for(int i = 0; i < NumLights; i++) {
					LightInfo lightInfo = LightInfos[i];

					if(abs(lightInfo.layer - currentLayer) > 3) continue; // Ignore lights that are not in the same layer or the next one

					// Send a ray to the light source
					float3 lightDir = normalize(lightInfo.position - hitInfo.hitPoint);

					Ray lightRay;
					lightRay.origin = hitInfo.hitPoint + hitInfo.normal * 0.001;
					lightRay.dir = lightDir;
					HitInfo lightHit = IterativeRayPropagationThroughPortals(hitInfo.layerOfHit, lightRay);

					float distanceToLight = length(lightInfo.position - hitInfo.hitPoint);
					float distanceToHit = length(lightHit.hitPoint - hitInfo.hitPoint);

					// In the case that a light source is in another room, but there is another light on the same ray path
					if(distanceToHit > distanceToLight) continue;
					if(distanceToHit + lightInfo.radius + 0.01f < distanceToLight) continue;

					/// The ray is guaranteed to hit something (either light or other), so we can calculate the light
					RayTracingMaterial material2 = lightHit.material;
					float NdotL = max(dot(hitInfo.normal, lightDir), 0.0);
					float3 emittedLight2 = material2.emissionColor * material2.emissionStrength;
					float attenuation = 1.0 / (distanceToLight * distanceToLight + 1e-4);
					float3 emittedLight3 = emittedLight2 * rayColor * NdotL * attenuation;
					
				 	incomingLight += emittedLight3;
					
				}

				incomingLight += GetAmbientLight(ray) * rayColor;
				
				return incomingLight;
			}

			
			// --- Show BVH Depth Function ---
			float4 showBVHDepthColor(Ray ray, float3 pixelCol) {
				if(!showBVHDepth) return float4(pixelCol, 1); // If we are not showing the BVH depth, return the pixel color

				int2 stack[16]; // Stack to hold BVH node indices
				int stackIndex = 0; // Stack index to keep track of the current position in the stack

				int layerIndex = cameraLayer; // Get the current layer index (0-based)

				stack[stackIndex++] = int2(RoomInfos[layerIndex].bvhNodesIndex, 1); // push root node of room onto the stack with depth 1

				float3 accumulatedColor = pixelCol;

				// Traverse the BVH tree iteratively
				while(stackIndex > 0) {
					int2 current = stack[--stackIndex];
					int nodeIndex = current.x;
					int depth = current.y;

					BVHNodeInfo node = BvhNodeInfos[nodeIndex];

					if(RayBoundingBox(ray, node.minBounds, node.maxBounds)) {
						if(accumulateBVHColors){
							if (depth <= bvhDepth) {
								accumulatedColor += float3(0.01, 0.01, 0.01); // Use a fixed color for accumulation
							}
						} else {
							if (depth == bvhDepth) {
								float3 boxColor = redToBlueColor(nodeIndex+2, NumBVHNodes);
								accumulatedColor = lerp(accumulatedColor, boxColor, 0.5);
								continue; // Don't push children if we're at the target depth
							}
						}

						if(!node.isLeaf) {
							stack[stackIndex++] = int2(node.leftChildIndex, depth + 1);
							stack[stackIndex++] = int2(node.rightChildIndex, depth + 1);
						}
					}
				}
				return float4(accumulatedColor, 1);
			}



            // Run for every pixel in the display:

            float4 frag(v2f i) : SV_Target {

				// Create seed for random number generator
                uint2 numPixels = _ScreenParams.xy;
                uint2 pixelCoord = i.uv * numPixels;
                uint pixelIndex = pixelCoord.y * numPixels.x + pixelCoord.x;
                uint rngState = pixelIndex;

                // Create Ray
                float3 viewPointLocal = float3(i.uv - 0.5, 1) * ViewParams;
                float3 viewPoint = mul(CamLocalToWorldMatrix, float4(viewPointLocal, 1));
                Ray ray;
                ray.origin = _WorldSpaceCameraPos;
                ray.dir = normalize(viewPoint - ray.origin);
				ray.invDir = 1 / ray.dir; // Precompute the inverse direction for faster calculations

				float3 pixelCol = 0;
				numIntersectionTests = 0;

				// ------ Simple Shapes Option -----
                if(UseSimpleShape){
					return IterativeRayPropagationThroughPortals(cameraLayer, ray).material.color;
                }

				// ------ Ray Tracing -----
				if(useRayTracing) {
					pixelCol += RayTrace(ray, rngState, cameraLayer);
				} 
				
				// ------ Importance Sampling -----
				if(useImportanceSampling) {
					pixelCol += ImportanceSample(ray, cameraLayer, nextLayer);
				}
				
				// ------ Show BVH Depth -----
				if(showBVHDepth) {
					pixelCol += showBVHDepthColor(ray, pixelCol);
				}

				// ------ Show Interesection Count (Debug) -----
				if(ShowIntersectionCount) {
					float debugColor = numIntersectionTests / float(maxIntersectionTests);
					return debugColor < 1 ? float4(debugColor, debugColor, debugColor, 1) : float4(1,0,0,1); // Debug color
				} 
				
				return float4(pixelCol, 1); // Return the pixel color			
	        }

            ENDCG

        }

    }
}
