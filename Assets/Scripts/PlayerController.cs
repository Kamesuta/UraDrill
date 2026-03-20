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
        public float jumpForce = 15f;
        public float rotationSpeed = 15f; // 壁の角度に合わせる回転速度
        
        [Header("Drill")]
        public float drillSpeed = 5f;
        public LayerMask groundLayer;
        
        private Rigidbody2D rb;
        private Animator animator;
        private Collider2D col;

        private bool isGrounded;
        private Vector2 surfaceNormal = Vector2.up;
        private bool isDrilling;
        
        private float moveInput;
        private bool jumpPressed;
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
                    
                if (Keyboard.current.spaceKey.wasPressedThisFrame)
                    jumpPressed = true;

                // Shiftキーで掘削開始
                if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
                    drillPressed = true;
            }

            // Gamepad入力
            if (Gamepad.current != null)
            {
                moveInput += Gamepad.current.leftStick.x.ReadValue();
                if (Gamepad.current.buttonSouth.wasPressedThisFrame)
                    jumpPressed = true;
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
            // 複数のレイキャストで周囲の地形を探索し、角（直角など）でも安定して吸着するようにする
            Vector2 origin = (Vector2)transform.position;
            Vector2 down = (Vector2)(-transform.up);
            Vector2 currentRight = (Vector2)transform.right;

            // 足元センサー：中心、左側、右側
            RaycastHit2D hitC = Physics2D.Raycast(origin, down, 1.5f, groundLayer);
            RaycastHit2D hitL = Physics2D.Raycast(origin - currentRight * 0.4f, down, 1.5f, groundLayer);
            RaycastHit2D hitR = Physics2D.Raycast(origin + currentRight * 0.4f, down, 1.5f, groundLayer);
            
            // 正面のセンサー（内角の90度に向かって歩いている時の吸着対策）
            float lookDir = moveInput != 0 ? Mathf.Sign(moveInput) : 0;
            RaycastHit2D hitF = lookDir != 0 ? Physics2D.Raycast(origin, currentRight * lookDir, 0.8f, groundLayer) : new RaycastHit2D();

            RaycastHit2D bestHit = new();
            
            // 優先順位：目の前の壁 > 足元中央 > 足元左右
            if (hitF.collider != null) bestHit = hitF;
            else if (hitC.collider != null) bestHit = hitC;
            else if (hitL.collider != null) bestHit = hitL;
            else if (hitR.collider != null) bestHit = hitR;

            if (bestHit.collider != null)
            {
                isGrounded = true;
                surfaceNormal = bestHit.normal;
            }
            else
            {
                // どのセンサーも地面を検知していない場合は空中状態
                isGrounded = false;
                // 空中では徐々に世界の真上（Vector2.up）に向きを戻す
                surfaceNormal = Vector2.Lerp(surfaceNormal, Vector2.up, Time.fixedDeltaTime * 2f);
            }

            // 1. キャラクターの向きを接地面の法線に合わせる
            Quaternion targetRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);

            // 2. 移動速度の計算
            // 現在の速度から、キャラクターの頭方向（Vertical）の速度成分を取り出す
            float verticalSpeed = Vector2.Dot(rb.linearVelocity, transform.up);
            
            // 重力の適用（加速度として計算）
            float gravityAccel = isGrounded ? 20f : 30f; // 重力加速度
            verticalSpeed -= gravityAccel * Time.fixedDeltaTime;

            // 接地している場合、下方向への速度を一定に制限（めり込み・摩擦防止）
            if (isGrounded && verticalSpeed < -2f)
            {
                verticalSpeed = -2f; 
            }

            // ジャンプの処理
            if (jumpPressed && isGrounded)
            {
                // 垂直速度をジャンプ力で直接書き換える
                verticalSpeed = jumpForce;
                isGrounded = false;
            }
            jumpPressed = false;

            // 水平移動速度を計算（キャラクターのローカル右方向）
            Vector2 horizontalVelocity = (Vector2)transform.right * (moveInput * moveSpeed);
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
            rb.linearVelocity = (Vector2)(-transform.up) * 2f;
        }
    }
}