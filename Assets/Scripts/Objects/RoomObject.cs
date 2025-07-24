using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RoomObject : MonoBehaviour {
    public int layer;

    public int numSpheres;
    public int spheresIndex; // Index of the first sphere in the list 
    
    public int numMeshes;
    public int meshIndex; // Index of the first mesh in the list

    public int numStencils;
    public int stencilIndex; // Index of the first stencil in the list
}
