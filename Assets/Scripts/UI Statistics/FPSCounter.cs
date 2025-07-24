using UnityEngine;
using TMPro;

public class FPSCounter : MonoBehaviour {
    public TextMeshProUGUI fpsText;
    private float deltaTime = 0.0f;
    bool isActive;

    void Update() {
        if(!isActive) return;
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        float fps = 1.0f / deltaTime;
        fpsText.text = Mathf.Ceil(fps).ToString() + " FPS";
    }

    public void toggleable(bool isActive) {
        this.isActive = isActive;
        if(isActive) {
            fpsText.gameObject.SetActive(true);
        } else {
            fpsText.gameObject.SetActive(false);
        }
    }
}