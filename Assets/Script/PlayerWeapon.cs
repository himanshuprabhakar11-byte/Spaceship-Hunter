using JetBrains.Annotations;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerWeapon : MonoBehaviour
{
    [SerializeField] GameObject[] lasers ;
    [SerializeField] RectTransform crosshair;

    [SerializeField] Transform targetPoint;

    [SerializeField] float targetDistance = 100f;


    bool isFiring = false;


    void Start()
    {
        Cursor.visible = false;
    }

    private void Update()
    {
        ProcessFiring();
        MoveCrosshair();
        MoveTargetPoint();
    }
    public void OnFire(InputValue value)
    {
        isFiring = value.isPressed;
    }

    void ProcessFiring()

    {
        foreach (GameObject laser in lasers)
        {
            var emmissionModule = laser.GetComponent<ParticleSystem>().emission;
            //Or you can use var and ParticleSystem.EmissionModule
            emmissionModule.enabled = isFiring;     
        }
    }

    void MoveCrosshair()
    {
        crosshair.position = Mouse.current.position.ReadValue();
    }

    void MoveTargetPoint()
    {// First, read the current mouse position (this returns a Vector2)
        Vector2 mousePos = Mouse.current.position.ReadValue();

        // Then, construct your Vector3 using those new input values
        Vector3 targetPointPosition = new Vector3(mousePos.x, mousePos.y, targetDistance);
        targetPoint.position = Camera.main.ScreenToWorldPoint(targetPointPosition); 
    }

}






