using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BVHNode {

    public Vector3 minBounds;
    public Vector3 maxBounds;
    
    public bool isLeaf; 

    public int startWrapperIndex;
    public int lengthOfWrappers;

    public int leftChildIndex;
    public int rightChildIndex;

    public int layer; 


    public BVHNode(List<WrapperObject> wrapperObjects, int startWrapperIndex, int lengthOfWrappers) {
        this.isLeaf = true;
        this.startWrapperIndex = startWrapperIndex;
        this.lengthOfWrappers = lengthOfWrappers;

        for(int i = startWrapperIndex; i < startWrapperIndex + lengthOfWrappers; i++) {
            WrapperObject wrapper = wrapperObjects[i];
            if (i == startWrapperIndex) {
                minBounds = wrapper.minBounds;
                maxBounds = wrapper.maxBounds;
            } else {
                minBounds = Vector3.Min(minBounds, wrapper.minBounds);
                maxBounds = Vector3.Max(maxBounds, wrapper.maxBounds);
            }
        }
    }

    public Vector3 CalculateBoundsSize(){
        return maxBounds - minBounds;
    }

}
