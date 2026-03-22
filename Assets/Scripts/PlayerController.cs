using System.Collections.Generic;
using LitMotion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    public class PlayerController : MonoBehaviour
    {
        // ワールド座標とセル座標の変換に使う Grid。
        [Header("Grid")]
        [SerializeField] private Grid grid;
        // 実際の地形判定は Collider ではなく Tilemap の occupied cell だけを見る。
        [SerializeField] private Tilemap groundTilemap;

        // すべてグリッド移動だが、見た目だけは少し滑らかにしたいので移動時間を持つ。
        [Header("Timing")]
        [SerializeField] private float moveDuration = 0.12f;
        // 左右を押しっぱなしにした時の次ステップまでの待ち時間。
        [SerializeField] private float moveRepeatDelay = 0.12f;
        // ドリル中は1セルずつ短いモーションで連続再生する。
        [SerializeField] private float drillStepDuration = 0.05f;

        // 境界セルの判定に使う4方向。
        // surfaceNormal もこのいずれかを取る前提で実装している。
        private static readonly Vector2Int[] Cardinals = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        // 次の候補セルは前後左右 + 斜めも見る。
        // 角を回り込む時に斜め候補が必要になる。
        private static readonly Vector2Int[] Neighbors = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left, new(1, 1), new(1, -1), new(-1, -1), new(-1, 1) };

        // 子にぶら下がっている Drill アニメーション制御用。
        private Animator animator;
        // 現在プレイヤーが存在している「空セル」。
        // 地面セルの上ではなく、地面に接する境界セルを歩く。
        private Vector3Int currentCell;
        // 現在の地形面から見た「上方向」。
        // 例えば地面の左壁にいるなら left、天井にいるなら down。
        private Vector2Int surfaceNormal = Vector2Int.up;
        // 左右入力。最終的に -1〜1 に正規化して使う。
        private float moveInput;
        // ドリル入力は押したフレームだけ有効なフラグとして扱う。
        private bool drillPressed;
        // 押しっぱなし移動の次回実行可能時刻。
        private float nextMoveTime;
        // 移動中・ドリル中の再入力を止めるためのフラグ。
        private bool isBusy;
        // 現在再生中の LitMotion ハンドル。
        // 新しいモーションを始める時に必ずキャンセルする。
        private MotionHandle activeMotion;
        // ドリルで通過するセル列。
        // 地面セルを抜けた先の空セルまでをまとめて保持する。
        private List<Vector3Int> drillPath;
        // drillPath の今どこまで進んだか。
        private int drillPathIndex;
        // ドリルの進行方向。surfaceNormal の逆向き。
        private Vector2Int drillDirection;

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();

            // 起動時のワールド位置から一番近い境界セルへスナップして状態を確定する。
            SnapToNearestBoundary();
        }
        private void Update()
        {
            // 毎フレームまず入力を読む。
            ReadInput();

            // 移動中・参照不足時は新しい操作を受け付けない。
            if (isBusy || grid == null || groundTilemap == null) return;

            // ドリルが押されたら通常移動より優先して処理する。
            if (drillPressed && TryStartDrill()) return;
            if (Mathf.Abs(moveInput) < 0.5f)
            {
                // 入力が離れたら連続移動の待ち時間もリセットする。
                nextMoveTime = 0f;
                return;
            }

            // 押しっぱなし移動の速度制御。
            if (Time.time < nextMoveTime) return;

            // 「進みたい向きに近く」「地形に接している」次セルを探す。
            if (!TryGetNextStep(moveInput > 0f ? 1 : -1, out var nextCell, out var nextNormal)) return;
            nextMoveTime = Time.time + moveRepeatDelay;

            // 見た目の補間が終わったら論理座標を更新する。
            StartMove(nextCell, nextNormal, moveDuration, () =>
            {
                currentCell = nextCell;
                surfaceNormal = nextNormal;
                isBusy = false;
            });
        }
        private void ReadInput()
        {
            moveInput = 0f;
            drillPressed = false;

            // キーボードは左右矢印か A/D、ドリルは Left Shift。
            if (Keyboard.current != null)
            {
                if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed) moveInput -= 1f;
                if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed) moveInput += 1f;
                if (Keyboard.current.leftShiftKey.wasPressedThisFrame) drillPressed = true;
            }

            // ゲームパッドも同じ意味で重ねる。
            if (Gamepad.current != null)
            {
                moveInput += Gamepad.current.leftStick.x.ReadValue();
                if (Gamepad.current.buttonWest.wasPressedThisFrame) drillPressed = true;
            }

            // 両入力が混ざっても扱いが崩れないように最後に Clamp。
            moveInput = Mathf.Clamp(moveInput, -1f, 1f);
        }
        private bool TryGetNextStep(int direction, out Vector3Int bestCell, out Vector2Int bestNormal)
        {
            // surfaceNormal に直交するベクトルが「面に沿った進行方向」になる。
            Vector2Int tangent = GetTangent(surfaceNormal) * direction;
            float bestScore = float.NegativeInfinity;
            bestCell = currentCell;
            bestNormal = surfaceNormal;

            // 近傍候補をなめて、最も「前に進めそう」で「今の面と連続性が高い」セルを選ぶ。
            foreach (var offset in Neighbors)
            {
                Vector3Int candidate = currentCell + ToCell(offset);

                // 地面セルには入れない。プレイヤーは常に空セル側を移動する。
                if (HasGround(candidate)) continue;
                foreach (var normal in Cardinals)
                {
                    // 候補セルがどこかの地面に接していないなら境界セルではない。
                    if (!HasGround(candidate - ToCell(normal))) continue;

                    // スコアは前進成分を最優先しつつ、
                    // 今の面法線に近いものをやや優先して不自然な折れを減らす。
                    // 凹角では前進成分が 0 の候補も必要になるので、ここでは即除外せず減点に留める。
                    float forward = Vector2.Dot(((Vector2)offset).normalized, tangent);
                    float score = forward * 10f;
                    score += Vector2.Dot(normal, surfaceNormal) * 2f;
                    score += offset.sqrMagnitude == 1 ? 0.25f : 0f;
                    if (forward < 0f) score += forward * 4f;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestCell = candidate;
                    bestNormal = normal;
                }
            }
            return bestScore > float.NegativeInfinity;
        }
        private bool TryStartDrill()
        {
            // ドリルは常に地面の中へ向かって掘る。
            drillDirection = -surfaceNormal;
            Vector3Int cell = currentCell + ToCell(drillDirection);

            // 目の前が空なら掘る意味がないので開始しない。
            if (!HasGround(cell)) return false;

            // 掘削ルートを先に全部作る。
            // 地面が続く間はそのまま進み、最初の空セルで止める。
            drillPath = new List<Vector3Int>();
            do
            {
                cell += ToCell(drillDirection);
                drillPath.Add(cell);
            }
            while (HasGround(cell));

            // ここからは連続モーションとして再生する。
            isBusy = true;
            drillPathIndex = 0;
            SetDrilling(true);
            PlayNextDrillStep();
            return true;
        }
        private void StartMove(Vector3Int targetCell, Vector2Int targetNormal, float duration, System.Action onComplete)
        {
            // 単発移動の薄いラッパー。状態ロックはここでまとめる。
            isBusy = true;
            AnimateTo(targetCell, targetNormal, duration, onComplete);
        }
        private void PlayNextDrillStep()
        {
            // ドリルは1セルずつ再生して、完了コールバックで次へ進む。
            Vector3Int step = drillPath[drillPathIndex];

            // 最後の1歩だけ、抜けた先の面法線へ回転させる。
            Vector2Int stepNormal = drillPathIndex == drillPath.Count - 1 ? drillDirection : surfaceNormal;
            AnimateTo(step, stepNormal, drillStepDuration, () =>
            {
                drillPathIndex++;
                if (drillPathIndex < drillPath.Count)
                {
                    PlayNextDrillStep();
                    return;
                }

                // 全ステップ完了後に論理位置と向きを確定する。
                currentCell = step;
                surfaceNormal = drillDirection;
                drillPath = null;
                SetDrilling(false);
                isBusy = false;
            });
        }
        private void AnimateTo(Vector3Int targetCell, Vector2Int targetNormal, float duration, System.Action onComplete)
        {
            // LitMotion には直接 Transform + Quaternion を渡さず、
            // 0→1 の progress を作って位置と回転を自前補間する。
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            Vector3 endPos = GetCellCenter(targetCell);
            Quaternion endRot = Quaternion.FromToRotation(Vector3.up, ToWorld(targetNormal));

            // 前のモーションが残っていたら必ず停止する。
            activeMotion.TryCancel();
            activeMotion = LMotion.Create(0f, 1f, duration)
                .WithOnComplete(() =>
                {
                    // 終了時は誤差を残さず、セル中心と角度をぴったり合わせる。
                    transform.SetPositionAndRotation(endPos, endRot);
                    onComplete?.Invoke();
                })
                .Bind(progress =>
                {
                    // progress に応じて見た目だけ滑らかに補間する。
                    transform.SetPositionAndRotation(
                        Vector3.Lerp(startPos, endPos, progress),
                        Quaternion.Slerp(startRot, endRot, progress));
                });
        }
        private void SnapToNearestBoundary()
        {
            // 起点の周囲 3x3 から、一番近い「空いていて地面に接しているセル」を探す。
            Vector3Int origin = grid.WorldToCell(transform.position);
            float bestDistance = float.PositiveInfinity;
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    Vector3Int cell = origin + new Vector3Int(x, y, 0);
                    if (HasGround(cell)) continue;
                    foreach (var normal in Cardinals)
                    {
                        // cell - normal に地面があるなら、そのセルは境界セルとして採用可能。
                        if (!HasGround(cell - ToCell(normal))) continue;
                        float distance = Vector3.Distance(transform.position, GetCellCenter(cell));
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        currentCell = cell;
                        surfaceNormal = normal;
                    }
                }
            }

            // 論理状態が決まったら見た目もそこへ揃える。
            transform.SetPositionAndRotation(
                GetCellCenter(currentCell),
                Quaternion.FromToRotation(Vector3.up, ToWorld(surfaceNormal)));
        }
        // 面法線から右方向接線を作る。
        // up -> right, right -> down, down -> left, left -> up になる。
        private Vector2Int GetTangent(Vector2Int normal) => new(normal.y, -normal.x);
        // Vector2Int を Tilemap 用のセル座標へ変換する。
        private Vector3Int ToCell(Vector2Int value) => new(value.x, value.y, 0);
        // surfaceNormal を回転計算に使うため 3D ベクトル化する。
        private Vector3 ToWorld(Vector2Int value) => new(value.x, value.y, 0f);
        // そのセルに地形タイルがあるかどうか。
        private bool HasGround(Vector3Int cell) => groundTilemap != null && groundTilemap.HasTile(cell);
        // セル中心のワールド座標を返す。
        private Vector3 GetCellCenter(Vector3Int cell) => grid.GetCellCenterWorld(cell);
        private void SetDrilling(bool value)
        {
            // Animator が無い構成でも落ちないように null を許容する。
            if (animator != null) animator.SetBool("Drilling", value);
        }
        private void OnDisable()
        {
            // 無効化時にモーションだけ残ると位置ズレの原因になるので必ず止める。
            activeMotion.TryCancel();
            SetDrilling(false);
            isBusy = false;
        }
    }
}
