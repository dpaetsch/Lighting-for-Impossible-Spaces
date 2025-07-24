using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct BVHNodeInfo {
    public Vector3 minBounds;
    public Vector3 maxBounds;
    public int isLeaf;  // 1 if leaf, 0 if not leaf
    public int startWrapperIndex;
    public int lengthOfWrappers;
    public int leftChildIndex;
    public int rightChildIndex;
}
