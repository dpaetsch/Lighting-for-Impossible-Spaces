Shader "Custom/RayTracing"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex); 
                o.uv = v.uv;
                return o;
            }

            // --- Settings and constants ---
			static const float PI = 3.1415;


            // RayTracing Settings
            int MaxBounceCount;
			int NumRaysPerPixel;

            // Camera Settings
            float3 ViewParams;
			float4x4 CamLocalToWorldMatrix;

            int UseSimpleShape;


            // Environment Settings
            int EnvironmentEnabled;
            float3 GroundColor;
            float3 SkyColorHorizon;
            float3 SkyColorZenith;
            float SunIntensity;
            float SunFocus;

			// Ambient Light
			int useAmientLight;
			float3 AmbientLightColor;
			float AmbientLightIntensity;


			// Stencil Buffer Settings
			int cameraLayer; // Layer of the camera
            
            // --- Structures ---
			struct Ray {
				float3 origin;
				float3 dir;
			};

            struct RayTracingMaterial {
				float4 color;
                float4 emissionColor;
				float4 specularColor;
				float emissionStrength;
				float smoothness;
				float specularProbability;
				int flag;
			};

            struct Sphere {
				float3 position;
				float radius;
				RayTracingMaterial material;
				int layer;
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

			 struct StencilRect {
				float3 center; // Center position of the rectangle
				float3 normal; // Normal vector defining orientation
				float3 u; // First basis vector (width direction)
				float3 v; // Second basis vector (height direction)
				int layer;
				int nextLayer; 
			};


			struct Room {
				int layer;
				int spheresIndex;    
				int numSpheres;
				int meshIndex;
				int numMeshes;
				int numStencils;
				int stencilIndex;
			};


			struct Triangle {
				float3 posA, posB, posC;
				float3 normalA, normalB, normalC;
				int layer;
			};

            struct MeshInfo {
				uint firstTriangleIndex;
				uint numTriangles;
				RayTracingMaterial material;
				float3 boundsMin;
				float3 boundsMax;
				int layer;
			};



            // --- Buffers ---
            StructuredBuffer<Sphere> Spheres;
            int NumSpheres;

            StructuredBuffer<Triangle> Triangles;
			StructuredBuffer<MeshInfo> AllMeshInfo;
			int NumMeshes;

			StructuredBuffer<StencilRect> StencilRects;
			int NumStencilRects;

			StructuredBuffer<Room> Rooms;
			int NumRooms;


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


            // Crude sky color function for background light
			float3 GetEnvironmentLight(Ray ray) {
				if (!EnvironmentEnabled) { return 0; }

				float skyGradientT = pow(smoothstep(0, 0.4, ray.dir.y), 0.35);
				float groundToSkyT = smoothstep(-0.01, 0, ray.dir.y);
				float3 skyGradient = lerp(SkyColorHorizon, SkyColorZenith, skyGradientT);

				float sun = pow(max(0, dot(ray.dir, _WorldSpaceLightPos0.xyz)), SunFocus) * SunIntensity;
				// Combine ground, sky, and sun
				float3 composite = lerp(GroundColor, skyGradient, groundToSkyT) + sun * (groundToSkyT>=1);
				return composite;
			}


			float3 GetAmbientLight(){
				if(!useAmientLight) { return 0; }
				float3 ambientLight = AmbientLightColor * AmbientLightIntensity;
				return ambientLight;
			}





            // --- Ray Intersection Functions ---
		
			// Calculate the intersection of a ray with a sphere
			HitInfo RaySphere(Ray ray, Sphere sphere) {
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
			// Thanks to https://stackoverflow.com/a/42752998
			HitInfo RayTriangle(Ray ray, Triangle tri) {
				float3 edgeAB = tri.posB - tri.posA;
				float3 edgeAC = tri.posC - tri.posA;
				float3 normalVector = cross(edgeAB, edgeAC);
				float3 ao = ray.origin - tri.posA;
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
				hitInfo.normal = normalize(tri.normalA * w + tri.normalB * u + tri.normalC * v);
				hitInfo.dst = dst;
				//hitInfo.material = tri.material;
				hitInfo.layerOfHit = tri.layer;
				return hitInfo;
			}

			// Thanks to https://gist.github.com/DomNomNom/46bb1ce47f68d255fd5d
			bool RayBoundingBox(Ray ray, float3 boxMin, float3 boxMax) {
				float3 invDir = 1 / ray.dir;
				float3 tMin = (boxMin - ray.origin) * invDir;
				float3 tMax = (boxMax - ray.origin) * invDir;
				float3 t1 = min(tMin, tMax);
				float3 t2 = max(tMin, tMax);
				float tNear = max(max(t1.x, t1.y), t1.z);
				float tFar = min(min(t2.x, t2.y), t2.z);
				return tNear <= tFar;
			};


			HitInfo RayRectangle(Ray ray, StencilRect rect) {
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
			HitInfo closestSphereHit(Ray ray, HitInfo bufferHit, int currentLayer){ 
				HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF;

				//Raycast against all spheres in the current layer and keep info about the closest hit
                for (int i = 0; i < NumSpheres; i++) {
					Sphere sphere = Spheres[i];

					HitInfo hitInfo = RaySphere(ray, sphere);

					// Check if there is something in the current layer that is closer than the buffer (if it's not lighting)
					if(hitInfo.didHit && hitInfo.dst < closestHit.dst && sphere.layer == currentLayer) {
						closestHit = hitInfo; // Captures the closest hit location, material, and layer
						//closestHit.material = sphere.material;
						//closestHit.layerOfHit = sphere.layer;
					}
				}
				//closestHit.material.color = float4(1, 0, 0, 1); // Debug color for the closest hit
				return closestHit;
			}


			HitInfo closestMeshHit(Ray ray, int currentLayer){			
				HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF; // We haven't hit anything yet, so 'closest' hit is infinitely far away

				// Raycast against all meshes in the current layer and keep info about the closest hit
				int layerIndex = currentLayer-1;
				int numMeshes = Rooms[layerIndex].numMeshes;
				int firstMeshIndex = Rooms[layerIndex].meshIndex;

				for(int meshIndex = firstMeshIndex; meshIndex < firstMeshIndex + numMeshes; meshIndex++) {
					MeshInfo meshInfo = AllMeshInfo[meshIndex];
					// Ignore mesh if it is not in the current layer (invisible)
					if(meshInfo.layer != currentLayer) { continue; }
					// Skip the mesh if ray doesn't intersect its bounding box.
					if (!RayBoundingBox(ray, meshInfo.boundsMin, meshInfo.boundsMax)) { continue; }
					//closestHit.material.color = float4(1, 0, 0, 1); // Debug color for the closest hit
					for (uint i = 0; i < meshInfo.numTriangles; i++) {
						int triIndex = meshInfo.firstTriangleIndex + i;
						Triangle tri = Triangles[triIndex];
						HitInfo hitInfo = RayTriangle(ray, tri);
						if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
							closestHit = hitInfo; // Captures the closest hit location, material, and layer
							closestHit.material = meshInfo.material;
							//closestHit.material.color = float4(1, 1, 0, 1); // Debug color for the closest hit
							closestHit.layerOfHit = meshInfo.layer;
						}
					}
				}
				return closestHit;
			}


			// Check if there is a buffer, in the path of the ary, in the current layer 
			HitInfo checkClosestBuffer(Ray ray, int currentLayer, int previousLayer){
				// Raycast against all rectangles in the current layer and keep info about the closest hit
				HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF;  // We haven't hit anything yet, so 'closest' hit is infinitely far away


				int layerIndex = currentLayer-1;
				int numStencils = Rooms[layerIndex].numStencils;
				int firstStencilIndex = Rooms[layerIndex].stencilIndex;

				for(int stencilIndex = firstStencilIndex; stencilIndex < firstStencilIndex + numStencils; stencilIndex++) {
					StencilRect rect = StencilRects[stencilIndex];

					// We ignore any buffer that is not part of the current layer
					if(rect.layer != currentLayer) { continue; }

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


			HitInfo IterativeRayPropagationThroughPortals(int startLayer, Ray ray) {
				HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF;

				int currentLayer = startLayer;
				int nextLayer;
				int previousLayer = 0;

				int maxIterations = 3;

				while (maxIterations-- > 0) {
					// Find the closest buffer hit in this layer
					HitInfo bufferHit = checkClosestBuffer(ray, currentLayer, previousLayer);
					bool thereIsBuffer = bufferHit.didHit;
					nextLayer = bufferHit.nextLayerIfBuffer;

					// Check for objects in the current layer
					HitInfo closestHitMesh = closestMeshHit(ray, currentLayer);
					if (closestHitMesh.didHit && closestHitMesh.dst < closestHit.dst) {
						closestHit = closestHitMesh;
					}

					// Check for spheres in the current layer	
					if(NumSpheres > 0){	
						HitInfo closestHitSphere = closestSphereHit(ray, bufferHit, currentLayer);
						if (closestHitSphere.didHit && closestHitSphere.dst < closestHit.dst) {
							closestHit = closestHitSphere;
						}
					}

					// If no buffer was hit, just do current layer
					if (!thereIsBuffer) {
						return closestHit;
					}

					// If we hit an object before the buffer, return the hit
					if (closestHit.didHit && closestHit.dst < bufferHit.dst) {
						return closestHit;
					}

					// If we hit a buffer, check the next layer
					// Move to the next layer and continue the search
					previousLayer = currentLayer;
					currentLayer = nextLayer;

				}
				return closestHit; // Return the closest hit found
			}


            float3 Trace(Ray ray, inout uint rngState, int currentLayer) {
				float3 incomingLight = 0;
				float3 rayColor = 1;	

				int StartOfLightingLayer = cameraLayer;

				for (int bounceIndex = 0; bounceIndex <= MaxBounceCount; bounceIndex++) {

					HitInfo hitInfo = IterativeRayPropagationThroughPortals(StartOfLightingLayer, ray);

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
							incomingLight += GetAmbientLight() * rayColor;
						}

					} else {
                        incomingLight += GetEnvironmentLight(ray) * rayColor;
						break;
					}
				}

				return incomingLight;
			}



            // Run for every pixel in the display:

            float4 frag(v2f i) : SV_Target {

				// ------ Simple Shapes Option -----
				// Just Color:
                if(UseSimpleShape){
                    float3 viewPointLocal = float3(i.uv - 0.5, 1) * ViewParams;
                    float3 viewPoint = mul(CamLocalToWorldMatrix, float4(viewPointLocal, 1));
                    Ray ray;
                    ray.origin = _WorldSpaceCameraPos;
                    ray.dir = normalize(viewPoint - ray.origin);
                    //return CalculateRayCollision(ray, false).material.color;
					//return IterativeRayPropagationThroughPortals(currentLayer, ray).material.color;
					return IterativeRayPropagationThroughPortals(cameraLayer, ray).material.color;
					//return float4(1, 0, 0, 1); // Debug color
                }


				// ------ Ray Tracing -----
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

                //Calculate Pixel Color
                float3 totalIncomingLight = 0;

                for (int i = 0; i < NumRaysPerPixel; i++) {
                    totalIncomingLight += Trace(ray, rngState, cameraLayer);
                }

                float3 pixelCol = totalIncomingLight / NumRaysPerPixel;
                return float4(pixelCol, 1); 
               

            }

            ENDCG

        }

    }
}
