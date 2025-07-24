using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RoomShifter : MonoBehaviour {

    public int setCurrentLayer;
    public int setNextLayer;


    public RoomObject deactivateRoom;
    public RoomObject activateRoom;

    

    private void OnTriggerEnter(Collider other){
        if (other.CompareTag("Player")) {
            RoomManager roomManager = FindObjectOfType<RoomManager>();
            if (roomManager != null) {
                Debug.Log("RoomManager found, setting layers.");
                roomManager.manager.currentLayer = setCurrentLayer;
                roomManager.manager.nextLayer = setNextLayer;
            }

            RoomObject[] roomObjects = FindObjectsOfType<RoomObject>();
            System.Array.Sort(roomObjects, (a, b) => a.layer.CompareTo(b.layer));


            
            // Deactivate the specified room
            if (deactivateRoom != null) {
                deactivateRoom.setActive(false);
                Debug.Log($"Deactivated room: {deactivateRoom.name}");
            } else {
                Debug.LogWarning("Deactivate room is not set.");
            }

            // Activate the specified room
            if (activateRoom != null) {
                activateRoom.setActive(true);
                Debug.Log($"Activated room: {activateRoom.name}");
            } else {
                Debug.LogWarning("Activate room is not set.");
            }





        }
    }
}
