// Assets/Scripts/Player/PlayerController.cs
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
    public PlayerInputControls InputControls;
    private PlayerMotor motor;


    // ================================================================
    //  初始化
    // ================================================================

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
    }

    void Start()
    {
        // ★ 本地模式：NetworkObject 未被 Spawn，OnNetworkSpawn 不会触发
        if (!IsSpawned)
        {
            InitPlayer();
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            Debug.Log("不是控制者");
            return;
        }

        InitPlayer();
    }

    // ★ 本地和联机共用的初始化逻辑
    private void InitPlayer()
    {
        InputControls = new PlayerInputControls();
        InputControls.Gameplay.Enable();

        Transform camTarget = transform.Find("CameraTarget");
        var freeLook = FindObjectOfType<Cinemachine.CinemachineFreeLook>();
        if (freeLook != null && camTarget != null)
        {
            freeLook.Follow = camTarget;
            freeLook.LookAt = transform;
        }
    }
    void Update()
    {
        // ★ 联机非 Owner 不处理；本地模式 (IsSpawned=false) 放行
        if (IsSpawned && !IsOwner) return;

        motor.InputDirection = InputControls.Gameplay.Move.ReadValue<Vector3>();
        if (InputControls.Gameplay.Jump.triggered)
        {
            motor.JumpRequest = true;
        }
        
    }
}


    
