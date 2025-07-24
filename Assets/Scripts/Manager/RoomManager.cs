using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RoomManager : MonoBehaviour {

    public ViewManager manager;

    public GameObject player; // Reference to the player object

    public int NumberOfRoomsActiveAtStart = 2;
    
    void Start() {

        // Find all RoomObjects in the scene
        RoomObject[] roomObjects = FindObjectsOfType<RoomObject>();

        System.Array.Sort(roomObjects, (a, b) => a.layer.CompareTo(b.layer));
        
        // Initialize each RoomObject
        foreach (RoomObject room in roomObjects) {
            room.setActive(false); 
        }

        manager.currentLayer = 0; // Set the current layer to 0
        manager.nextLayer = 1; // Set the next layer to 1 (for player movement)
        

        for(int i = 0; i < NumberOfRoomsActiveAtStart && i < roomObjects.Length; i++){
            roomObjects[i].setActive(true);
        }


        player = GameObject.FindGameObjectWithTag("Player");

        // teleport player to the first room
        if (player != null) {
            Transform playerTransform = player.transform;
            playerTransform.position = transform.position;
            playerTransform.rotation = transform.rotation;
            //playerTransform.position = roomObjects[0].transform.position + new Vector3(0, -8, 0); // Adjust height as needed
            //playerTransform.rotation = Quaternion.identity; // Reset rotation
        } else {
            Debug.LogError("Player object not found. Please ensure the player has the 'Player' tag.");
        }
    }

    
    void Update() {
        
    }

}
