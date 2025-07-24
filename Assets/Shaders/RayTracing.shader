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


			// Stencil Buffer Settings
			int currentLayer; // Layer of the camera
			int currentLightingLayer; // Layer that we are currently in, in order to calculate bounce light


            
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

            struct Triangle {
				float3 posA, posB, posC;
				float3 normalA, normalB, normalC;
				int layer;
				int IsStencilBuffer;
				int nextLayerIfBuffer;
			};

            struct MeshInfo {
				uint firstTriangleIndex;
				uint numTriangles;
				RayTracingMaterial material;
				float3 boundsMin;
				float3 boundsMax;
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
				//RayTracingMaterial material; 
				int layer;
				int nextLayer; 
			};


			struct Room {
				int layer;
				int firstSphereIndex;  // Start index in Spheres buffer
				int numSpheres;
				int firstMeshIndex; // Start index in Triangles buffer
				int numMeshes;
			};



            // --- Buffers ---
            StructuredBuffer<Sphere> Spheres;
            int NumSpheres;

            StructuredBuffer<Triangle> Triangles;
			StructuredBuffer<MeshInfo> AllMeshInfo;
			int NumMeshes;


			StructuredBuffer<StencilRect> StencilRects;
			int NumStencilRects;


			// StructuredBuffer<Room> Rooms;
			// int NumRooms;
			



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

			// Check if there is a buffer, in the path of the ary, in the current layer 
			HitInfo checkClosestBuffer(Ray ray, int currentLightingLayer){
				// Raycast against all rectangles in the current layer and keep info about the closest hit
				HitInfo closestHit = (HitInfo)0;

				// We haven't hit anything yet, so 'closest' hit is infinitely far away
				closestHit.dst = 1.#INF;

				for (int i = 0; i < NumStencilRects; i++) {
					StencilRect rect = StencilRects[i];

					if(rect.layer != currentLightingLayer) { continue; }

					HitInfo hitInfo = RayRectangle(ray, rect);

					if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
						closestHit = hitInfo;
						//closestHit.material = rect.material;
						closestHit.nextLayerIfBuffer = rect.nextLayer; // Record the next layer if buffer is hit
					}
				}

				return closestHit;
			}




			HitInfo closestSphereHit (Ray ray, HitInfo bufferHit, bool isLighting, int tempNextLightingLayer){ 
				HitInfo closestHit = (HitInfo)0;

				bool thereIsBuffer = bufferHit.didHit;	

				// We haven't hit anything yet, so 'closest' hit is infinitely far away
				closestHit.dst = 1.#INF;
				//Raycast against all spheres in the current layer and keep info about the closest hit
                for (int i = 0; i < NumSpheres; i++) {
					Sphere sphere = Spheres[i];


					if(!isLighting){ // if ray is just used to determine objects from the camera's view
						if(!thereIsBuffer){ // there is no buffer in the direction of the ray ( => Display everything in the current layer)
							if(sphere.layer != currentLayer) { continue; } // ignore spheres in other layers

							HitInfo hitInfo = RaySphere(ray, sphere);

							if (hitInfo.didHit && hitInfo.dst < closestHit.dst ) {
								closestHit = hitInfo;
								//closestHit.material = sphere.material;
								//closestHit.layerOfHit = sphere.layer;
							}
						} else { // There is a buffer in the direction of the ray

							HitInfo hitInfo = RaySphere(ray, sphere);

							// Check if there is something in the current layer that is closer than the buffer (if it's not lighting)
							if(hitInfo.didHit && hitInfo.dst < closestHit.dst && sphere.layer == currentLayer) {
								closestHit = hitInfo; // Captures the closest hit location, material, and layer
								//closestHit.material = sphere.material;
								//closestHit.layerOfHit = sphere.layer;
							}
							// Check if hits triangle, it is farther away than buffer, and it is the closest hit, and it is not buffer
							else if (hitInfo.didHit && hitInfo.dst > bufferHit.dst &&  hitInfo.dst < closestHit.dst && sphere.layer == bufferHit.nextLayerIfBuffer) {
								closestHit = hitInfo; // Captures the closest hit location, material, and layer
								//closestHit.material = sphere.material;
								//closestHit.layerOfHit = sphere.layer;
							} 
						}
					} else { // Ray is used for coloring of a pixel (light propagation)
						if(!thereIsBuffer){ // There is no buffer in the direction of the ray and it is lighting ( => Get Cloests Hit of the object if it is in the current lighting layer)
							if(sphere.layer != currentLightingLayer ) { continue; }

							HitInfo hitInfo = RaySphere(ray, sphere);

							if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
								closestHit = hitInfo; // Captures the closest hit location, material, and layer
								//closestHit.material = sphere.material;
								//closestHit.layerOfHit = sphere.layer;
							}

						} else { // There is a buffer in the direction of the ray (and it is lighting)
							//if(tri.IsStencilBuffer) { continue; } // ignore any buffers.

							HitInfo hitInfo = RaySphere(ray, sphere);

							// Check if while in current lighting  layer, there is something closer than buffer and it is in the same layer and it is not buffer and it is closest hit
							if (hitInfo.didHit && hitInfo.dst < closestHit.dst && hitInfo.dst < bufferHit.dst && sphere.layer == currentLightingLayer) {
								closestHit = hitInfo; // Captures the closest hit location, material, and layer
								//closestHit.material = sphere.material;
								//closestHit.layerOfHit = sphere.layer;
							}

							//Check if while in current lighting layer, if hits triangle that is farther away than the buffer, and it is the closest hit, and it is not buffer
							else if (hitInfo.didHit && hitInfo.dst < closestHit.dst && hitInfo.dst > bufferHit.dst && sphere.layer == bufferHit.nextLayerIfBuffer && sphere.layer != currentLightingLayer) {
								closestHit = hitInfo; // Captures the closest hit location, material, and layer
								//closestHit.material = sphere.material;
								// closestHit.layerOfHit = sphere.layer;
							}
							
						}
					}

				}
				return closestHit;
			}


			HitInfo closestMeshHit(Ray ray, HitInfo bufferHit, bool isLighting, int tempNextLightingLayer){			
				HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF; // We haven't hit anything yet, so 'closest' hit is infinitely far away

				bool thereIsBuffer = bufferHit.didHit; 

                // Raycast against all meshes and keep info about the closest hit
				for (int meshIndex = 0; meshIndex < NumMeshes; meshIndex++) {
					MeshInfo meshInfo = AllMeshInfo[meshIndex];

                    // Skip the mesh if ray doesn't intersect its bounding box.
					if (!RayBoundingBox(ray, meshInfo.boundsMin, meshInfo.boundsMax)) { continue; }

					for (uint i = 0; i < meshInfo.numTriangles; i++) {
						int triIndex = meshInfo.firstTriangleIndex + i;
						Triangle tri = Triangles[triIndex];

						if(!isLighting){ // if ray is just used to determine objects from the camera's view
							if(!thereIsBuffer){ // there is no buffer in the direction of the ray ( => Display everything in the current layer)
								if(tri.layer != currentLayer) { continue; }

								HitInfo hitInfo = RayTriangle(ray, tri);

								if (hitInfo.didHit && hitInfo.dst < closestHit.dst ) {
									closestHit = hitInfo; // Captures the closest hit location, material, and layer
									closestHit.material = meshInfo.material;
									//closestHit.layerOfHit = tri.layer;
									//tempNextLightingLayer = tri.layer;
								}
							} else { // There is a buffer in the direction of the ray
								//if(tri.IsStencilBuffer) { continue; } // ignore any buffers.

								HitInfo hitInfo = RayTriangle(ray, tri);

								// Check if there is something in the current layer that is closer than the buffer (if it's not lighting)
								if(hitInfo.didHit && hitInfo.dst < closestHit.dst && tri.layer == currentLayer) {
									closestHit = hitInfo; // Captures the closest hit location, material, and layer
									closestHit.material = meshInfo.material;
									// closestHit.layerOfHit = tri.layer;
									//tempNextLightingLayer = tri.layer;
								}
								// Check if hits triangle, it is farther away than buffer, and it is the closest hit, and it is not buffer
								else if (hitInfo.didHit && hitInfo.dst > bufferHit.dst &&  hitInfo.dst < closestHit.dst && tri.layer == bufferHit.nextLayerIfBuffer && !tri.IsStencilBuffer) {
									closestHit = hitInfo; // Captures the closest hit location, material, and layer
									closestHit.material = meshInfo.material;
									//closestHit.layerOfHit = tri.layer;
									//tempNextLightingLayer = tri.layer; // This is the layer after on the other side of buffer
								} 
							}
						} else { // Ray is used for coloring of a pixel (light propagation)
							if(!thereIsBuffer){ // There is no buffer in the direction of the ray and it is lighting ( => Get Cloests Hit of the object if it is in the current lighting layer)
								if(tri.layer != currentLightingLayer ) { continue; }

								HitInfo hitInfo = RayTriangle(ray, tri);

								if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
									closestHit = hitInfo; // Captures the closest hit location, material, and layer
									closestHit.material = meshInfo.material;
									// closestHit.layerOfHit = tri.layer;
								}

							} else { // There is a buffer in the direction of the ray (and it is lighting)
								//if(tri.IsStencilBuffer) { continue; } // ignore any buffers.

								HitInfo hitInfo = RayTriangle(ray, tri);

								// Check if while in current lighting  layer, there is something closer than buffer and it is in the same layer and it is not buffer and it is closest hit
								if (hitInfo.didHit && hitInfo.dst < closestHit.dst && hitInfo.dst < bufferHit.dst && tri.layer == currentLightingLayer) {
									closestHit = hitInfo; // Captures the closest hit location, material, and layer
									closestHit.material = meshInfo.material;
									// closestHit.layerOfHit = tri.layer;
								}

								//Check if while in current lighting layer, if hits triangle that is farther away than the buffer, and it is the closest hit, and it is not buffer
								else if (hitInfo.didHit && hitInfo.dst < closestHit.dst && hitInfo.dst > bufferHit.dst && tri.layer == bufferHit.nextLayerIfBuffer && tri.layer != currentLightingLayer) {
									closestHit = hitInfo; // Captures the closest hit location, material, and layer
									closestHit.material = meshInfo.material;
									// closestHit.layerOfHit = tri.layer;
								}
								
							}
						}	
					}
				}


				return closestHit;
			}


		







			// --- Ray Collision Calculation ---

            // Find the first point that the given ray collides with, and return hit info
            HitInfo CalculateRayCollision(Ray ray, bool isLighting) {

				// Closest hit (objects)
                HitInfo closestHit = (HitInfo)0;
				closestHit.dst = 1.#INF;
				
				// Cloest hit (buffer) -> so we know what's in front and what's behind
				HitInfo bufferHit = (HitInfo)0;
				bufferHit.dst = 1.#INF;
				bool thereIsBuffer;

				int nextLayerIfBuffer = 0;

				// Temporary variable to store the next lighting layer (if there is a buffer)
				int tempNextLightingLayer = currentLightingLayer;
				
				
				// Check if there is buffer (in the current lighting layer)
				bufferHit = checkClosestBuffer(ray, currentLightingLayer); // First pass, it is currentLayer, but it will be updated to currentLightingLayer in the next pass
				thereIsBuffer = bufferHit.didHit;
				nextLayerIfBuffer = bufferHit.nextLayerIfBuffer;
				
				// Check if there is a sphere in the current layer
				if(NumSpheres > 0){
					HitInfo closestHitSphere = closestSphereHit(ray, bufferHit, isLighting, nextLayerIfBuffer);
					if(closestHitSphere.didHit && closestHitSphere.dst < closestHit.dst){
						closestHit = closestHitSphere;
						tempNextLightingLayer = closestHitSphere.layerOfHit;
					}
				}
				
				// Check if there is a mesh in the current layer
				HitInfo closestHitMesh = closestMeshHit(ray, bufferHit, isLighting, nextLayerIfBuffer);
				if(closestHitMesh.didHit && closestHitMesh.dst < closestHit.dst){
					closestHit = closestHitMesh;
					tempNextLightingLayer = closestHitMesh.layerOfHit;
				}
				
				currentLightingLayer = tempNextLightingLayer;

                return closestHit;
            }




            float3 Trace(Ray ray, inout uint rngState, int currentLayer) {
				float3 incomingLight = 0;
				float3 rayColor = 1;

				// Boolean to check if the ray is used for lighting or just object visibility
				bool isLighting = false;

				// reset the lighting layer to current layer (no necessarily true for all pixels, but good for now)
				currentLightingLayer = currentLayer;

				for (int bounceIndex = 0; bounceIndex <= MaxBounceCount; bounceIndex++) {

					// Check if the ray is for lighting or if it is just for object visibility
					if(bounceIndex > 0) isLighting = true; 

					HitInfo hitInfo = CalculateRayCollision(ray, isLighting);

					if (hitInfo.didHit) {
                        ray.origin = hitInfo.hitPoint;
                        //ray.dir = RandomHemisphereDirection(hitInfo.normal, rngState);
                        ray.dir = normalize(hitInfo.normal + RandomDirection(rngState));
                        
						RayTracingMaterial material = hitInfo.material;
						float3 emittedLight = material.emissionColor * material.emissionStrength;
                        //float lightStrength = dot(hitInfo.normal,ray.dir); 
                        incomingLight += emittedLight * rayColor;
                        rayColor *= material.color; //* lightStrength * 2;
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
                    return CalculateRayCollision(ray, false).material.color;
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
                    totalIncomingLight += Trace(ray, rngState, currentLayer);
                }

                float3 pixelCol = totalIncomingLight / NumRaysPerPixel;
                return float4(pixelCol, 1); 
               

            }

            ENDCG

        }

    }
}
