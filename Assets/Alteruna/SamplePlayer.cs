using UnityEngine;
using Alteruna.Multiplayer;
using Alteruna.Multiplayer.Core;
using Alteruna.Multiplayer.Unity;

public class SamplePlayer : CommunicationBridge
{

    public float Speed = 5f;
    public float Sensitivity = 200;
    
    private Transform _camera;

    private void Start()
    {
        _camera = GetComponentInChildren<Camera>().transform;
    }

    public override void Possessed(bool isMe, User user)
    {
        // disables this script for remote players
        enabled = isMe;
    }

    public void Update()
    {
        UpdateMovement();
        UpdateRotation();
    }

    private void UpdateMovement()
    {
        // Get horizontal input
        var input = Input.GetAxisRaw("Horizontal") * transform.right;

        // Get vertical input
        input += Input.GetAxisRaw("Vertical") * transform.forward;
        
        if (input.magnitude < 0.01f) return;
        
        // Normalize input
        input /= input.magnitude;
        
        // Scale input
        input *= Speed * Time.deltaTime;
        
        // Apply input
        transform.Translate(input, Space.World);
    }

    private void UpdateRotation()
    {
        float sensitivity = Sensitivity * Time.deltaTime;
		
        // Vertical mouse look
        float mouseX = Input.GetAxis("Mouse X");
        transform.Rotate(0, mouseX * sensitivity, 0);
		
        // vertical mouse look
        
        // Get current camera rotation
        Vector3 cameraRot = _camera.localEulerAngles;
        // Add mouse movement
        cameraRot.x -= Input.GetAxis("Mouse Y") * sensitivity;
        // Fix overrotation issues
        if (cameraRot.x > 180f) cameraRot.x -= 360f;
        // Clamp input so you can't look behind you
        cameraRot.x = Mathf.Clamp(cameraRot.x, -89f, 89f);
        // apply rotation
        _camera.localEulerAngles = cameraRot;
    }
}
