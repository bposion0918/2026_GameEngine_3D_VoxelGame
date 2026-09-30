using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("기본 이동 및 중력")]
    public float moveSpeed = 5f;
    public float jumpPower = 5f;
    public float gravity = -20f;

    [Header("카메라 설정")]
    public float mouseSensitivity = 0.2f;
    public Transform cameraPivot;

    [Header("구르기 설정 (Shift)")]
    public float rollSpeed = 10f; // 구르는 동안의 이동 속도
    public float rollDuration = 0.5f; // 구르는 시간 (0.5초)
    public float rollCooldown = 2f; // 구르기 쿨타임 (2초)

    // 상태 관리 변수 (다른 공격 스크립트에서 참조할 수 있도록 public 설정)
    [HideInInspector] public bool isRolling = false;
    [HideInInspector] public bool isInvincible = false; // 무적 상태
    [HideInInspector] public bool canAttack = true;     // 공격 가능 여부

    // 내부 계산용 변수
    private Vector2 moveInput;
    private Vector2 lookInput;
    private float pitch = 20f;
    private float verticalVelocity;

    private float rollTimer = 0f;
    private float rollCooldownTimer = 0f;
    private Vector3 rollDirection;

    private CharacterController controller;

    void Start()
    {
        // 2강 내용: CharacterController 가져오기 및 마우스 커서 숨기기[cite: 5, 6]
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
    }

    // 2강, 3강 내용: Input System을 통한 입력 받기[cite: 5, 6]
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnLook(InputValue value)
    {
        lookInput = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        // 구르는 중에는 점프 불가 조건 추가
        if (value.isPressed && controller.isGrounded && !isRolling)
        {
            verticalVelocity = jumpPower;
        }
    }

    // 유니티 기본 Input Action에서는 Shift가 'Sprint'로 설정되어 있습니다.
    public void OnSprint(InputValue value)
    {
        // 키를 눌렀고, 쿨타임이 다 돌았으며, 현재 구르지 않고 있고, 바닥에 있을 때 발동
        if (value.isPressed && rollCooldownTimer <= 0f && !isRolling && controller.isGrounded)
        {
            StartRoll();
        }
    }

    private void StartRoll()
    {
        isRolling = true;
        isInvincible = true; // 무적 켜기
        canAttack = false;   // 공격 불가

        rollTimer = rollDuration;
        rollCooldownTimer = rollCooldown;

        // 구르기 방향 설정 (현재 이동하려는 방향, 가만히 있으면 앞을 향해 구름)
        if (moveInput != Vector2.zero)
        {
            rollDirection = (transform.forward * moveInput.y + transform.right * moveInput.x).normalized;
        }
        else
        {
            rollDirection = transform.forward;
        }
    }

    void Update()
    {
        // 1. 마우스 시점 회전 (3강 내용)
        pitch = pitch - lookInput.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, -20f, 60f); // 위아래 각도 제한
        transform.Rotate(0f, lookInput.x * mouseSensitivity, 0f);
        cameraPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);

        // 2. 중력 계산 (2강 내용)[cite: 5]
        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        verticalVelocity += gravity * Time.deltaTime;

        // 3. 이동 및 구르기 로직
        Vector3 move = Vector3.zero;

        if (isRolling)
        {
            // 구르는 중일 때의 동작
            rollTimer -= Time.deltaTime;
            move = rollDirection * rollSpeed; // 정해진 방향으로 빠르게 이동

            if (rollTimer <= 0f)
            {
                // 구르기 종료 (원래 상태로 복구)
                isRolling = false;
                isInvincible = false;
                canAttack = true;
            }
        }
        else
        {
            // 일반 걷기 상태일 때의 동작
            if (rollCooldownTimer > 0f)
            {
                rollCooldownTimer -= Time.deltaTime; // 쿨타임 감소
            }

            // 3강 내용: 바라보는 방향을 기준으로 이동[cite: 6]
            move = transform.forward * moveInput.y + transform.right * moveInput.x;
            move = move * moveSpeed;
        }

        // 수직 속도(점프, 중력) 적용[cite: 5]
        move.y = verticalVelocity;

        // 최종 이동 적용[cite: 5]
        controller.Move(move * Time.deltaTime);
    }
}