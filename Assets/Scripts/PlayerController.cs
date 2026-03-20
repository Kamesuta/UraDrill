using UnityEngine;
using UnityEngine.InputSystem;

namespace VerbGame 
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 5f;
        public float rotationSpeed = 28f; // 角でも素早く追従する回転速度

        [Header("Surface Following")]
        public float gravityAcceleration = 30f;
        public float maxStickForce = 4f;
        public float surfaceProbeRadius = 0.35f;
        public float surfaceProbeDistance = 1.2f;
        public float airRealignSpeed = 6f;
        [Range(0f, 90f)]
        public float forwardSnapAngle = 35f;

        [Header("Drill")]
        public float drillSpeed = 5f;
        public LayerMask groundLayer;
        
        private Rigidbody2D rb;
        private Animator animator;
        private Collider2D col;

        private bool isGrounded;
        private Vector2 surfaceNormal = Vector2.up;
        private Vector2 desiredUp = Vector2.up;
        private bool isDrilling;
        
        private float moveInput;
        private bool drillPressed;

        void Start()
        {
            rb = GetComponent<Rigidbody2D>();
            col = GetComponent<Collider2D>();
            // 子オブジェクトのAnimatorを取得（Drillオブジェクトにアタッチされているもの）
            animator = GetComponentInChildren<Animator>();
            
            // 回転はTransformを直接操作して制御するため、物理挙動による回転は固定する
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            // 壁歩きのためにカスタム重力をスクリプト内で適用するので、標準の重力は0にする
            rb.gravityScale = 0; 
        }

        void Update()
        {
            HandleInput();
            
            // アニメーション更新 (DrillingのBool値を更新して採掘アニメーションを制御)
            if (animator != null)
            {
                animator.SetBool("Drilling", isDrilling);
            }
        }

        private void HandleInput()
        {
            moveInput = 0f;

            // InputSystemを使用してキーボード入力を取得
            if (Keyboard.current != null)
            {
                if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed)
                    moveInput -= 1f;
                if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed)
                    moveInput += 1f;
                    
                // Shiftキーで掘削開始
                if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
                    drillPressed = true;
            }

            // Gamepad入力
            if (Gamepad.current != null)
            {
                moveInput += Gamepad.current.leftStick.x.ReadValue();
                if (Gamepad.current.buttonWest.wasPressedThisFrame)
                    drillPressed = true;
            }

            moveInput = Mathf.Clamp(moveInput, -1f, 1f);
        }

        void FixedUpdate()
        {
            if (isDrilling)
            {
                // 掘削中の移動更新
                DrillUpdate();
            }
            else
            {
                // 通常時（壁歩行含む）の移動更新
                NormalUpdate();
            }
        }

        private void NormalUpdate()
        {
            UpdateSurfaceNormal();

            // 1. キャラクターの向きを接地面の法線に合わせる
            Quaternion targetRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
            float rotateStep = rotationSpeed * Time.fixedDeltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateStep * 90f);

            // 2. 移動速度の計算
            // 現在の速度から、キャラクターの頭方向（Vertical）の速度成分を取り出す
            float verticalSpeed = Vector2.Dot(rb.linearVelocity, transform.up);
            
            // 重力の適用（加速度として計算）
            float gravityAccel = gravityAcceleration; // カスタム重力
            verticalSpeed -= gravityAccel * Time.fixedDeltaTime;

            // 接地している場合、下方向への速度を一定に制限（めり込み・摩擦防止）
            if (isGrounded && verticalSpeed < -maxStickForce)
            {
                verticalSpeed = -maxStickForce; 
            }

            // 接地面に沿った接線方向を算出（current normalに垂直な方向）
            Vector2 surfaceTangent = new Vector2(surfaceNormal.y, -surfaceNormal.x);
            if (Vector2.Dot(surfaceTangent, transform.right) < 0f)
            {
                surfaceTangent = -surfaceTangent;
            }

            // 水平移動速度を計算（接地面の接線方向で常に素早く曲がる）
            Vector2 horizontalVelocity = surfaceTangent.normalized * (moveInput * moveSpeed);
            // 最終的な速度を合成して適用
            rb.linearVelocity = horizontalVelocity + (Vector2)transform.up * verticalSpeed;

            // 掘削開始処理
            if (drillPressed && isGrounded)
            {
                StartDrilling();
            }
            drillPressed = false;
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            if (isDrilling) return;
            // 壁に接触している間は常に法線を更新し、ジャンプ中に壁に当たった時でも即座に張り付くようにする
            if (((1 << collision.gameObject.layer) & groundLayer) != 0)
            {
                surfaceNormal = collision.contacts[0].normal;
                desiredUp = surfaceNormal;
                isGrounded = true;
            }
        }

        private void StartDrilling()
        {
            isDrilling = true;
            // 掘削中は物理演算の影響を受けず直線的に進むため、Kinematicに変更
            rb.bodyType = RigidbodyType2D.Kinematic;
            // 地面を通り抜けられるようにトリガー化する
            col.isTrigger = true;
            rb.linearVelocity = Vector2.zero;
        }

        private void DrillUpdate()
        {
            // 向いている方向（ドリル方向）に一定速度で進む
            Vector3 drillDirection = -transform.up;
            rb.MovePosition(rb.position + (Vector2)(drillDirection * drillSpeed * Time.fixedDeltaTime));

            // 地面(Groundレイヤー)を完全に抜け出したかチェック
            if (!col.IsTouchingLayers(groundLayer))
            {
                StopDrilling();
            }
        }

        private void StopDrilling()
        {
            isDrilling = false;
            // 物理挙動を通常（Dynamic）に戻す
            rb.bodyType = RigidbodyType2D.Dynamic;
            col.isTrigger = false;
            // 抜け出した瞬間に少しだけ脱出の勢いを付ける
            Vector2 newSurface = (Vector2)(-transform.up);
            desiredUp = newSurface;
            surfaceNormal = newSurface;
            isGrounded = false;
            rb.linearVelocity = newSurface * 2f;
        }

        private void UpdateSurfaceNormal()
        {
            Vector2 origin = transform.position;
            Vector2 localDown = -transform.up;
            Vector2 localRight = transform.right;

            RaycastHit2D hitDown = Physics2D.CircleCast(origin, surfaceProbeRadius, localDown, surfaceProbeDistance, groundLayer);
            RaycastHit2D hitDiagL = Physics2D.CircleCast(origin, surfaceProbeRadius, (localDown - localRight).normalized, surfaceProbeDistance, groundLayer);
            RaycastHit2D hitDiagR = Physics2D.CircleCast(origin, surfaceProbeRadius, (localDown + localRight).normalized, surfaceProbeDistance, groundLayer);

            RaycastHit2D hitForward = new();
            if (Mathf.Abs(moveInput) > 0.01f)
            {
                Vector2 forward = localRight * Mathf.Sign(moveInput);
                hitForward = Physics2D.CircleCast(origin + forward * surfaceProbeRadius * 0.5f, surfaceProbeRadius * 0.6f, forward, surfaceProbeDistance * 0.8f, groundLayer);
            }

            RaycastHit2D groundHit = PickCloserHit(hitDown, PickCloserHit(hitDiagL, hitDiagR));
            RaycastHit2D bestHit = groundHit.collider != null ? groundHit : hitForward;

            bool allowForwardSnap = hitForward.collider != null
                                    && Mathf.Abs(moveInput) > 0.01f
                                    && Vector2.Angle(surfaceNormal, hitForward.normal) >= forwardSnapAngle;

            if (allowForwardSnap)
            {
                bestHit = PickCloserHit(groundHit, hitForward);
            }
            else if (bestHit.collider == null && hitForward.collider != null)
            {
                bestHit = hitForward;
            }

            if (bestHit.collider != null)
            {
                surfaceNormal = bestHit.normal;
                desiredUp = surfaceNormal;
                isGrounded = true;
            }
            else
            {
                isGrounded = false;
                surfaceNormal = Vector2.Lerp(surfaceNormal, desiredUp, Time.fixedDeltaTime * airRealignSpeed);
            }
        }

        private RaycastHit2D PickCloserHit(RaycastHit2D a, RaycastHit2D b)
        {
            if (a.collider == null) return b;
            if (b.collider == null) return a;
            return a.distance <= b.distance ? a : b;
        }
    }
}
