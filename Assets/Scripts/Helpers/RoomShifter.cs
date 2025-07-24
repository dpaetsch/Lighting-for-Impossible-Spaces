using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RoomShifter : MonoBehaviour {

    public int setCurrentLayer;
    public int setNextLayer;

    public RoomObject activateRoom;
    public RoomObject deactivateRoom;
   
   [ReadOnly] public RoomObject parentRoom;

    private void Start(){
        // Get the parent GameObject's RoomObject component
        if (transform.parent != null) {
            parentRoom = transform.parent.GetComponent<RoomObject>();
            if (parentRoom == null) {
                Debug.LogWarning("RoomShifter: Parent exists but does not have a RoomObject component.");
            }
        } else {
            Debug.LogWarning("RoomShifter: This object has no parent.");
        }
    }

    void OnValidate(){
        // Get the parent GameObject's RoomObject component
        if (transform.parent != null) {
            parentRoom = transform.parent.GetComponent<RoomObject>();
            if (parentRoom == null) {
                Debug.LogWarning("RoomShifter: Parent exists but does not have a RoomObject component.");
            }
        } else {
            Debug.LogWarning("RoomShifter: This object has no parent.");
        }
    }

    private void OnTriggerEnter(Collider other){
        if (other.CompareTag("Player")) {
            RoomManager roomManager = FindObjectOfType<RoomManager>();
            if (roomManager != null) {
                Debug.Log("Setting layers: " + setCurrentLayer + ", " + setNextLayer);
                roomManager.manager.currentLayer = setCurrentLayer;
                roomManager.manager.nextLayer = setNextLayer;
            }

            RoomObject[] roomObjects = FindObjectsOfType<RoomObject>();
            System.Array.Sort(roomObjects, (a, b) => a.layer.CompareTo(b.layer));
            
            // Deactivate the specified room
            if (deactivateRoom != null) {
                deactivateRoom.setActive(false);
                Debug.Log($"Deactivated room: {deactivateRoom.name}, from: {parentRoom.virtualizedLayer}");
            } else {
                //Debug.LogWarning("Deactivate room is not set.");
            }

            // Activate the specified room
            if (activateRoom != null) {
                activateRoom.setActive(true);
                Debug.Log($"Activated room: {activateRoom.name}, from: {parentRoom.virtualizedLayer}");
            } else {
               // Debug.LogWarning("Activate room is not set.");
            }

        }
    }
}
