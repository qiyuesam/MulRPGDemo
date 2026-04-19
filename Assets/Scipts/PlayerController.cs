using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    private Rigidbody rb;
    public PlayerInputControls InputControls;
    [SerializeField]
    private Vector3 inputDirection;
    private float moveSpeed=10f;
    private float smoothTime=0.1f;
    [SerializeField]
    private Vector3 currentVelocity=Vector3.zero;
    // Start is called before the first frame update
    void Start()
    {
        rb=GetComponent<Rigidbody>();
        InputControls=new PlayerInputControls();
        InputControls.Gameplay.Enable();
    }
    

    // Update is called once per frame
    void Update()
    {
        inputDirection=InputControls.Gameplay.Move.ReadValue<Vector3>();
    }

    void FixedUpdate()
    {
        Move();
    }

    void Move()
    {
        var targetDirection=new Vector3(inputDirection.x,0,inputDirection.z).normalized;
        if (targetDirection == Vector3.zero)
        {
            rb.velocity=new Vector3(0,rb.velocity.z,0);
            return;
        }
        Vector3 targetVelocity=targetDirection*moveSpeed;
        Vector3 smoothedVelocity = Vector3.SmoothDamp(
            new Vector3(rb.velocity.x, 0, rb.velocity.z),
            new Vector3(targetVelocity.x, 0, targetVelocity.z),
            ref currentVelocity,
            smoothTime,
            Mathf.Infinity,
            Time.fixedDeltaTime
        );
        rb.velocity = new Vector3(smoothedVelocity.x, rb.velocity.y, smoothedVelocity.z);
    }
    
    
}
