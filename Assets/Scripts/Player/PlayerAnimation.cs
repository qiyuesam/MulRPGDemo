using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAnimation : MonoBehaviour
{
    private PlayerMotor motor;
    private Animator animator;
    // Start is called before the first frame update

    private bool wasMoving;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        animator = GetComponentInChildren<Animator>();
        if(motor==null)
            Debug.LogError("PlayerMotor is null");
        if(animator==null)
            Debug.LogError("Animator is null");
    }
    
    
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (motor == null || animator == null) return;
        
        bool isMoving = motor.IsMoving;
        if (isMoving!=wasMoving)
        {
            animator.SetBool("IsMoving", isMoving);
            wasMoving = isMoving;
        }
    }
}
