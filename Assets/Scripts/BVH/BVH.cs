using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BVH {

    // General Idea:
    // This is a BVH (Bounding Volume Hierarchy) class that will be used to create a hierarchy of BVH Nodes.
    // BVH nodes will follow a binary tree structure where each node contains a bounding box that encompasses its children.
    // The leaf BVH nodes will contain a start index and a length of the wrappers that are contained in that node.
    // All BVH nodes will contain a bounding box that encompasses all the triangles and spheres in the scene.

    public MeshObject[] meshObjects;
    public SphereObject[] sphereObjects;

    public List<WrapperObject> wrapperObjects; //  represent wrappers for triangles and spheres
    public List<BVHNode> bvhNodes; // represents the BVH nodes in the hierarchy

    public int numWrapperObjects; // total number of wrapper objects in the BVH
    public int numBVHNodes; // total number of BVH nodes in the hierarchy

    public int maxDepth;
    public int maxDepthReached; // maximum depth reached in the BVH tree during construction

    public int roomMeshStartIndex; // index of the first mesh in the room
    public int roomSphereStartIndex; // index of the first sphere in the room
    public int roomMeshLength; // number of meshes in the room
    public int roomSphereLength; // number of spheres in the room

    public int roomLayer; // layer of the room, used for stencil buffer and rendering

    public bool useFullObjectsInBVH; // if true, the wrapper objects will point to the full objects (MeshObject or SphereObject) in the BVH, otherwise they will point to triangles and SphereObjects.

    public BVH(MeshObject[] meshObjects, int roomMeshStartIndex, int roomMeshLength, SphereObject[] sphereObjects, int roomSphereStartIndex, int roomSphereLength, int maxDepth, int roomLayer, bool useFullObjectsInBVH) {
        // We assume that the meshObjects and sphereObjects are already populated with the necessary data.
        // And that they only contain objects that are for this BVH (i.e. they are all from the same room).
        // depth is the maximum depth of the BVH tree, which can be used to control the granularity of the hierarchy.
        // depth = 0 there is no hierarchy
        // depth = 1 means 1 root node 
        // depth = 2 means 2 nodes (1 root and 2 children)

        // if useFullObjectsInBHV is true, 

        this.meshObjects = meshObjects;
        this.sphereObjects = sphereObjects;
        this.maxDepth = maxDepth;

        this.roomMeshStartIndex = roomMeshStartIndex;
        this.roomSphereStartIndex = roomSphereStartIndex;
        this.roomMeshLength = roomMeshLength;
        this.roomSphereLength = roomSphereLength;

        this.roomLayer = roomLayer;

        this.useFullObjectsInBVH = useFullObjectsInBVH;
        
        CreateWrapperObjects();
        CreateBVHStructure();
    }

    public void CreateWrapperObjects() {
        wrapperObjects = new List<WrapperObject>();
        int triangleIndex = 0;
        int meshIndex = 0;
        int sphereIndex = 0;

        // Wrapper Objects are different depending on useFullObjectsInBVH 


        if(useFullObjectsInBVH){
            // Create wrapper object for each mesh
            for(int i = roomMeshStartIndex; i < roomMeshStartIndex + roomMeshLength; i++){
                MeshObject meshObject = meshObjects[i];
                WrapperObject wrapperObject = new WrapperObject(meshObject, meshIndex);
                wrapperObject.layer = roomLayer; // Set the layer of the wrapper object
                wrapperObjects.Add(wrapperObject);
                meshIndex++;
            }       

        } else {
            // Create wrapper object for each triangle
            for(int i = roomMeshStartIndex; i < roomMeshStartIndex + roomMeshLength; i++){
                List<TriangleInfo> triangles = meshObjects[i].GetTriangles();
                for(int j = 0; j < meshObjects[i].triangleCount; j++){
                    TriangleInfo triangle = triangles[j];
                    WrapperObject wrapperObject = new WrapperObject(triangle, meshIndex, triangleIndex);
                    triangleIndex++;
                    wrapperObject.layer = roomLayer; // Set the layer of the wrapper object
                    wrapperObjects.Add(wrapperObject);
                }
                meshIndex++;
            }
        }
        
        // Create wrapper object for each sphere
        for (int i = roomSphereStartIndex; i < roomSphereStartIndex + roomSphereLength; i++) {
            SphereObject sphereObject = sphereObjects[i];
            WrapperObject wrapperObject = new WrapperObject(sphereObject, sphereIndex);
            sphereIndex++;
            wrapperObject.meshIndex = -1; // -1 since it's a sphere, not a triangle
            wrapperObject.layer = roomLayer; // Set the layer of the wrapper object
            wrapperObjects.Add(wrapperObject);
        }

        numWrapperObjects = wrapperObjects.Count;   
    }

    void CreateBVHStructure(){
        bvhNodes = new List<BVHNode>();
        numBVHNodes = 0;

        maxDepthReached = 0;

        // Create the root node
        BVHNode rootNode = new BVHNode(wrapperObjects, 0, numWrapperObjects);
        rootNode.layer = roomLayer; // Set the layer of the root node
        bvhNodes.Add(rootNode);
        numBVHNodes++;

        CreateBVHNodes(rootNode, 1);
    }


    void CreateBVHNodes(BVHNode parentNode, int depth) {
        if (depth >= maxDepth || parentNode.lengthOfWrappers <= 1) {
            // If we reached the maximum depth or there is only one wrapper, we make this a leaf node
            parentNode.isLeaf = true;
            maxDepthReached = Mathf.Max(maxDepthReached, depth);
            return;
        }

        Vector3 boundsSize = parentNode.CalculateBoundsSize();
        float parentCost = NodeCost(boundsSize, parentNode.lengthOfWrappers);

        (int splitAxis, float splitPos, float cost) = FindBestSplit(parentNode, parentNode.startWrapperIndex, parentNode.lengthOfWrappers);

        // we found the best split position and axis, now we need to create left and right child nodes
        if (cost < parentCost && depth < maxDepth) {

            int numOnLeft = 0;

            for(int i = parentNode.startWrapperIndex; i < parentNode.startWrapperIndex + parentNode.lengthOfWrappers; i++) {
                WrapperObject wrapper = wrapperObjects[i];

                // we need to swap the order of the wrappers
                if (wrapper.center[splitAxis] < splitPos) {
                    WrapperObject temp = wrapperObjects[parentNode.startWrapperIndex + numOnLeft];
                    wrapperObjects[parentNode.startWrapperIndex + numOnLeft] = wrapper;
                    wrapperObjects[i] = temp;
                    numOnLeft++;
                } 
            }

            // Create left and right child nodes
            BVHNode leftChild = new BVHNode(wrapperObjects, parentNode.startWrapperIndex, numOnLeft ); 
            BVHNode rightChild = new BVHNode(wrapperObjects, parentNode.startWrapperIndex + numOnLeft, parentNode.lengthOfWrappers - numOnLeft);

            parentNode.isLeaf = false; // This is no longer a leaf node
            leftChild.isLeaf = true;
            rightChild.isLeaf = true;
            leftChild.leftChildIndex = -1; // Initialize child indices to -1
            leftChild.rightChildIndex = -1;
            rightChild.leftChildIndex = -1;
            rightChild.rightChildIndex = -1;

            // Set the children of the parent node
            parentNode.leftChildIndex = bvhNodes.Count;
            leftChild.layer = parentNode.layer; // Set the layer of the left child node
            bvhNodes.Add(leftChild);
            numBVHNodes++;
            
            parentNode.rightChildIndex = bvhNodes.Count;
            rightChild.layer = parentNode.layer; // Set the layer of the right child node
            bvhNodes.Add(rightChild);
            numBVHNodes++;

            // Recursively create BVH nodes for children
            if( leftChild.lengthOfWrappers > 0) CreateBVHNodes(leftChild, depth + 1);
            if( rightChild.lengthOfWrappers > 0) CreateBVHNodes(rightChild, depth + 1);

        } else {
            // If we cannot split, we make this a leaf node
            parentNode.isLeaf = true;
        }
        
    }


    public List<BVHNode> GetBVHNodes() {
        return bvhNodes;
    }

    public List<WrapperObject> GetWrapperObjects() {
        return wrapperObjects;
    }


    // Using Surface AreasHeuristic (SAH)
    // input: size of boundingbox, and the number of wrappers in the node
    public float NodeCost(Vector3 size, int numWrappers) {
        // Computes half the surface area of an axis-aligned bounding box.
        float halfArea = size.x * size.y + size.x * size.z + size.y * size.z;

        return halfArea * numWrappers; // Cost is proportional to the surface area of the bounding box times the number of wrappers
    }

    // Finds the best axis and position to split the node into two child nodes
    // input: BVHNode node
    public (int axis, float pos, float cost) FindBestSplit(BVHNode node,int start,  int count) {
        if (count <= 1) return (0, 0f, float.MaxValue); // No split possible
        
        float bestSplitPos = 0;
        int bestSplitAxis = 0;
        int numSplitTests = 10;

        float bestCost = float.MaxValue;

        // Estimate best split pos
        for(int axis = 0; axis < 3; axis++){
            for(int i = 0; i < numSplitTests; i++) {

                float splitT = (i + 1) / (numSplitTests + 1f); // T value between 0 and 1
                float splitPos = Mathf.Lerp(node.minBounds[axis], node.maxBounds[axis], splitT);
                // Calculate cost of splitting at this position
                float cost = EvaluateSplit(axis, splitPos, start, count);

                if (cost < bestCost) {
                    bestCost = cost;
                    bestSplitPos = splitPos;
                    bestSplitAxis = axis;
                }
            }
        }

        return (bestSplitAxis, bestSplitPos, bestCost);
    }


    float EvaluateSplit(int axis, float splitPos, int start, int count) {
        // This function evaluates the cost of splitting the node at the given position along the specified axis.
        // It calculates the cost of the left and right child nodes and returns the total cost.

        // Count how many wrappers are on each side of the split
        int leftCount = 0;
        int rightCount = 0;

        Vector3 leftMin = Vector3.positiveInfinity;
        Vector3 leftMax = Vector3.negativeInfinity;
        Vector3 rightMin = Vector3.positiveInfinity;
        Vector3 rightMax = Vector3.negativeInfinity;

        for (int i = start; i < start + count; i++) {
            WrapperObject wrapper = wrapperObjects[i];
            if (wrapper.center[axis] < splitPos) {
                leftCount++;
                leftMin = Vector3.Min(leftMin, wrapper.minBounds);
                leftMax = Vector3.Max(leftMax, wrapper.maxBounds);
            } else {
                rightCount++;
                rightMin = Vector3.Min(rightMin, wrapper.minBounds);
                rightMax = Vector3.Max(rightMax, wrapper.maxBounds);
            }
        }

        float leftCost = NodeCost(leftMax - leftMin, leftCount);
        float rightCost = NodeCost(rightMax - rightMin, rightCount);

        return leftCost + rightCost;
    }



}
