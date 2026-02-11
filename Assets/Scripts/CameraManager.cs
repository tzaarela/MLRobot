using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public Camera camera1;
    public Camera camera2;

    void Start()
    {
        ActivateCamera1();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            ActivateCamera1();
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            ActivateCamera2();
        }
    }

    void ActivateCamera1()
    {
        camera1.enabled = true;
        camera2.enabled = false;
    }

    void ActivateCamera2()
    {
        camera1.enabled = false;
        camera2.enabled = true;
    }
}
