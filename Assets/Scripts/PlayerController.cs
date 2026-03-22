using LitMotion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VerbGame
{
    [RequireComponent(typeof(Collider2D))]
    public class PlayerController : MonoBehaviour
    {
        // プレイヤー制御は「通常移動」「掘削前スナップ」「掘削前の180度回転」「掘削中」「掘削後スナップ」の状態で管理する。
        private enum MovementState
        {
            Normal,
            SnapBeforeDrill,
            RotateBeforeDrill,
            Drilling,
            SnapAfterDrill,
        }

        [Header("Movement")]
        public float moveSpeed = 5f;
        public float rotationSpeed = 720f;

        [Header("Surface Following")]
        public float surfaceProbeRadius = 0.35f;
        public float surfaceProbeDistance = 1.2f;
        [Range(0f, 90f)]
        public float forwardSnapAngle = 35f;

        [Header("Grid Snap")]
        public float snapMoveSpeed = 8f;
        public float snapRotationSpeed = 720f;
        public float snapPositionThreshold = 0.01f;
        public float snapAngleThreshold = 0.5f;
        public Grid grid;

        [Header("Drill")]
        public float drillTurnDuration = 0.2f;
        public float drillSpeed = 5f;
        public float drillEnterDistance = 0.1f;
        public LayerMask groundLayer;

        // Drill 子オブジェクトのアニメーターを想定している。
        private Animator animator;
        // 物理移動はしないが、地形との重なり判定には Collider2D を使う。
        private Collider2D col;

        private MovementState movementState = MovementState.Normal;
        // 現在どこかの面に接しているかどうか。
        private bool isGrounded;
        // 今いる面の法線。これを基準に「上方向」と接線方向を決める。
        private Vector2 surfaceNormal = Vector2.up;
        // 一時的に接地判定を失っても、直前の向きを維持するための退避値。
        private Vector2 desiredUp = Vector2.up;
        // スナップ先のグリッド座標。
        private Vector3 snapTargetPosition;
        // スナップ完了時の回転。
        private Quaternion snapTargetRotation = Quaternion.identity;

        // 左右入力は -1 〜 1 に正規化して使う。
        private float moveInput;
        // 掘る入力はそのフレームで一度だけ消費する。
        private bool drillPressed;
        // 掘削開始時に確定した進行方向を保持する。
        private Vector2 drillDirection = Vector2.down;
        // アニメーションの Bool は状態と完全一致させず、フローの明示タイミングで切り替える。
        private bool isDrillAnimationPlaying;
        // LitMotion による180度回転ハンドル。
        private MotionHandle drillTurnHandle;

        void Start()
        {
            col = GetComponent<Collider2D>();
            animator = GetComponentInChildren<Animator>();

            // 初期向きを足元の面に合わせておく。
            UpdateSurfaceNormal();
            desiredUp = surfaceNormal;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
        }

        void Update()
        {
            // 入力取得、状態更新、見た目更新を順に行う。
            HandleInput();
            TickState(Time.deltaTime);

            // 押下イベントは毎フレーム末尾でクリアし、長押しで多重開始しないようにする。
            drillPressed = false;
        }

        void OnDisable()
        {
            drillTurnHandle.TryCancel();
            SetDrillingAnimation(false);
        }

        private void HandleInput()
        {
            moveInput = 0f;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed)
                    moveInput -= 1f;
                if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed)
                    moveInput += 1f;

                // Shift で掘削開始を予約する。
                if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
                    drillPressed = true;
            }

            if (Gamepad.current != null)
            {
                // キーボードとゲームパッドを足し合わせ、最後に Clamp する。
                moveInput += Gamepad.current.leftStick.x.ReadValue();
                if (Gamepad.current.buttonWest.wasPressedThisFrame)
                    drillPressed = true;
            }

            moveInput = Mathf.Clamp(moveInput, -1f, 1f);
        }

        private void TickState(float deltaTime)
        {
            // 現在の状態に応じて処理を分岐する。
            switch (movementState)
            {
                case MovementState.Normal:
                    NormalUpdate(deltaTime);
                    break;
                case MovementState.SnapBeforeDrill:
                case MovementState.SnapAfterDrill:
                    SnapUpdate(deltaTime);
                    break;
                case MovementState.RotateBeforeDrill:
                    break;
                case MovementState.Drilling:
                    DrillUpdate(deltaTime);
                    break;
            }
        }

        private void NormalUpdate(float deltaTime)
        {
            // 通常時は足元の面を検出し続け、面に沿って移動・回転する。
            UpdateSurfaceNormal();
            RotateTowardsSurface(deltaTime);
            MoveAlongSurface(deltaTime);

            // 掘削は接地している時だけ開始できる。
            if (drillPressed && isGrounded)
            {
                BeginPreDrillSnap();
            }
        }

        private void SnapUpdate(float deltaTime)
        {
            // 掘削前後とも、最終的な位置と向きへスムーズに吸着させる。
            Vector3 nextPosition = Vector3.MoveTowards(
                transform.position,
                snapTargetPosition,
                snapMoveSpeed * deltaTime);

            Quaternion nextRotation = Quaternion.RotateTowards(
                transform.rotation,
                snapTargetRotation,
                snapRotationSpeed * deltaTime);

            transform.SetPositionAndRotation(nextPosition, nextRotation);
            Physics2D.SyncTransforms();

            bool positionDone = Vector3.Distance(transform.position, snapTargetPosition) <= snapPositionThreshold;
            bool rotationDone = Quaternion.Angle(transform.rotation, snapTargetRotation) <= snapAngleThreshold;
            if (!positionDone || !rotationDone)
            {
                return;
            }

            transform.SetPositionAndRotation(snapTargetPosition, snapTargetRotation);
            Physics2D.SyncTransforms();

            if (movementState == MovementState.SnapBeforeDrill)
            {
                StartDrillRotation();
                return;
            }

            CompletePostDrillSnap();
        }

        private void StartDrillRotation()
        {
            // 掘削方向はスナップ直後の面法線から確定する。
            drillDirection = -surfaceNormal;
            movementState = MovementState.RotateBeforeDrill;

            Quaternion startRotation = transform.rotation;
            drillTurnHandle.TryCancel();
            drillTurnHandle = LMotion.Create(0f, 180f, drillTurnDuration)
                .WithEase(Ease.InOutSine)
                .WithOnComplete(BeginDrillAdvance)
                .Bind(angle =>
                {
                    transform.rotation = startRotation * Quaternion.Euler(0f, 0f, angle);
                });
        }

        private void BeginDrillAdvance()
        {
            // 180度回転が終わってからアニメーションを開始する。
            isDrillAnimationPlaying = true;
            SetDrillingAnimation(true);
            movementState = MovementState.Drilling;

            // 境界線ちょうどで開始すると重なり判定が不安定なので、少しだけ中へ押し込む。
            transform.position += (Vector3)(drillDirection * drillEnterDistance);
            Physics2D.SyncTransforms();
        }

        private void DrillUpdate(float deltaTime)
        {
            // 掘削中は開始時に確定した方向へ直進する。
            transform.position += (Vector3)(drillDirection * (drillSpeed * deltaTime));
            Physics2D.SyncTransforms();

            // 地形との重なりがなくなった瞬間を「反対側へ抜けた」とみなす。
            if (!IsOverlappingGround())
            {
                FinishDrillTraversal();
            }
        }

        private void FinishDrillTraversal()
        {
            // 反対側へ抜けたらアニメーションを止め、天井側の向きへ確定する。
            isDrillAnimationPlaying = false;
            SetDrillingAnimation(false);
            surfaceNormal = drillDirection;
            desiredUp = surfaceNormal;
            isGrounded = true;

            BeginPostDrillSnap();
        }

        private void BeginPreDrillSnap()
        {
            movementState = MovementState.SnapBeforeDrill;
            snapTargetPosition = GetNearestGridPosition(transform.position);
            snapTargetRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
        }

        private void BeginPostDrillSnap()
        {
            movementState = MovementState.SnapAfterDrill;
            snapTargetPosition = GetNearestGridPosition(transform.position);
            snapTargetRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
        }

        private void CompletePostDrillSnap()
        {
            movementState = MovementState.Normal;
        }

        private void RotateTowardsSurface(float deltaTime)
        {
            // キャラクターの上方向を面法線に合わせる。
            Quaternion targetRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * deltaTime);
        }

        private void MoveAlongSurface(float deltaTime)
        {
            // 接地していない、または入力が小さい場合は移動しない。
            if (!isGrounded || Mathf.Abs(moveInput) < 0.01f)
            {
                return;
            }

            // 法線に直交するベクトルが「面に沿った移動方向」になる。
            Vector2 surfaceTangent = new(surfaceNormal.y, -surfaceNormal.x);
            if (Vector2.Dot(surfaceTangent, transform.right) < 0f)
            {
                // キャラクターの右方向と揃えて左右入力の意味を安定させる。
                surfaceTangent = -surfaceTangent;
            }

            transform.position += (Vector3)(surfaceTangent.normalized * (moveInput * moveSpeed * deltaTime));
            Physics2D.SyncTransforms();
        }

        private Vector3 GetNearestGridPosition(Vector3 worldPosition)
        {
            // Grid のセル中心をスナップ先に使う。
            Vector3Int cell = grid.WorldToCell(worldPosition);
            Vector3 center = grid.GetCellCenterWorld(cell);
            center.z = worldPosition.z;
            return center;
        }

        private bool IsOverlappingGround()
        {
            // transform 直更新後でも確実に調べられる OverlapCollider を使う。
            ContactFilter2D filter = new()
            {
                useLayerMask = true,
                layerMask = groundLayer,
                useTriggers = false
            };

            Collider2D[] results = new Collider2D[8];
            return col.Overlap(filter, results) > 0;
        }

        private void SetDrillingAnimation(bool isPlaying)
        {
            if (animator != null)
            {
                animator.SetBool("Drilling", isPlaying);
            }
        }

        private void UpdateSurfaceNormal()
        {
            // 足元・斜め前下・斜め後下を調べ、今いる面の法線を推定する。
            Vector2 origin = transform.position;
            Vector2 localDown = -transform.up;
            Vector2 localRight = transform.right;

            RaycastHit2D hitDown = Physics2D.CircleCast(origin, surfaceProbeRadius, localDown, surfaceProbeDistance, groundLayer);
            RaycastHit2D hitDiagL = Physics2D.CircleCast(origin, surfaceProbeRadius, (localDown - localRight).normalized, surfaceProbeDistance, groundLayer);
            RaycastHit2D hitDiagR = Physics2D.CircleCast(origin, surfaceProbeRadius, (localDown + localRight).normalized, surfaceProbeDistance, groundLayer);

            RaycastHit2D hitForward = default;
            if (Mathf.Abs(moveInput) > 0.01f)
            {
                // 入力方向の少し前も調べて、角を曲がる候補を拾いやすくする。
                Vector2 forward = localRight * Mathf.Sign(moveInput);
                hitForward = Physics2D.CircleCast(
                    origin + 0.5f * surfaceProbeRadius * forward,
                    surfaceProbeRadius * 0.6f,
                    forward,
                    surfaceProbeDistance * 0.8f,
                    groundLayer);
            }

            RaycastHit2D groundHit = PickCloserHit(hitDown, PickCloserHit(hitDiagL, hitDiagR));
            RaycastHit2D bestHit = groundHit.collider != null ? groundHit : hitForward;

            // 現在の面と十分に角度差がある前方ヒットだけを「曲がり先候補」として採用する。
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
                // 面が見つかればその法線を現在値として採用する。
                surfaceNormal = bestHit.normal;
                desiredUp = surfaceNormal;
                isGrounded = true;
            }
            else
            {
                // 一時的にヒットが消えても向きは急変させず、直前の姿勢を保つ。
                isGrounded = false;
                surfaceNormal = desiredUp;
            }
        }

        private RaycastHit2D PickCloserHit(RaycastHit2D a, RaycastHit2D b)
        {
            // null を考慮しつつ、より近いヒットを返す小さなユーティリティ。
            if (a.collider == null) return b;
            if (b.collider == null) return a;
            return a.distance <= b.distance ? a : b;
        }
    }
}
