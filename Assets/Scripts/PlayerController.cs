using UnityEngine;
using UnityEngine.InputSystem;

namespace VerbGame 
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        public float moveSpeed = 5f;
        public float jumpForce = 10f;
        private Rigidbody2D rb;
        private bool isGrounded;

        void Start()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        private float moveInput;
        private bool jumpPressed;

        void Update()
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
            }

            // ゲームパッド入力もサポート
            if (Gamepad.current != null)
            {
                moveInput += Gamepad.current.leftStick.x.ReadValue();
                if (Gamepad.current.buttonSouth.wasPressedThisFrame)
                    jumpPressed = true;
            }

            moveInput = Mathf.Clamp(moveInput, -1f, 1f);
        }

        void FixedUpdate()
        {
            rb.linearVelocity = new Vector2(moveInput * moveSpeed, rb.linearVelocity.y);

            if (jumpPressed && isGrounded)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
                // ジャンプを処理したらフラグをリセット
                jumpPressed = false;
                isGrounded = false;
            }
            else
            {
                // 地上にいるかどうかにかかわらず、FixedUpdateごとにリセットして
                // 長期間ジャンプ入力が残るのを防ぐ
                jumpPressed = false;
            }
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            foreach (ContactPoint2D contact in collision.contacts)
            {
                if (contact.normal.y > 0.5f)
                {
                    isGrounded = true;
                    return;
                }
            }
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            isGrounded = false;
        }
    }
}