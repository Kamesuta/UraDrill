using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    // このクラスは「司令塔」だけを担当する。
    // 入力を読み、論理移動クラスに問い合わせ、
    // 最後に見た目クラスへ「こう動いて」と命令する。
    public class PlayerController : MonoBehaviour
    {
        // グリッド座標とワールド座標の相互変換に使う。
        [Header("Grid")]
        [SerializeField] private Grid grid;
        // 地形タイルの有無だけを見たいので Tilemap を直接参照する。
        [SerializeField] private Tilemap groundTilemap;
        [SerializeField] private WallPanelCatalog wallPanelCatalog;

        // 通常の1ステップ移動時間。
        [Header("Timing")]
        [SerializeField] private float moveDuration = 0.15f;
        // 押しっぱなし時の繰り返し間隔。
        [SerializeField] private float moveRepeatDelay = 0.12f;
        // ドリル開始前にその場で向きを作る時間。
        [SerializeField] private float drillRotateDuration = 0.12f;
        // ドリル中の1セルぶん移動時間。
        [SerializeField] private float drillStepDuration = 0.15f;
        // 氷で滑った時の落下1セルぶん移動時間。
        [SerializeField] private float slipFallStepDuration = 0.12f;

        // グリッド上の論理移動だけを扱うヘルパー。
        private PlayerGridNavigator navigator;
        // LitMotion と Animator をまとめた見た目担当。
        private PlayerView view;
        // 左右入力。最後に -1〜1 に正規化する。
        private float moveInput;
        // ドリル入力は押したフレームだけ有効。
        private bool drillPressed;
        // 押しっぱなし移動の次回解禁時刻。
        private float nextMoveTime;
        // アニメーション中に再入力で状態が壊れないようにするロック。
        private bool isBusy;

        private void Awake()
        {
            // ロジック担当と見た目担当をここで組み立てる。
            navigator = new PlayerGridNavigator(grid, groundTilemap, wallPanelCatalog);
            view = new PlayerView(transform, GetComponentInChildren<Animator>());

            // 初期位置は、最寄りの境界セルへ論理的にスナップしてから、
            // その結果を見た目へそのまま反映する。
            navigator.SnapToNearestBoundary(transform.position);
            view.SnapTo(navigator.GetCellCenter(navigator.CurrentCell), navigator.GetRotation(navigator.SurfaceNormal));
        }

        private void Update()
        {
            // 参照不足、または何かの演出中なら新しい操作は受け付けない。
            if (isBusy || grid == null || groundTilemap == null) return;

            // 氷の壁や天井には張り付けないので、入力より先に重力落下する。
            if (TryStartSlipFall()) return;

            // Jump は Unity Event で立てた1回分の要求として消費する。
            bool drillRequested = drillPressed;
            drillPressed = false;

            // ドリルは通常移動より優先。
            if (drillRequested && TryStartDrill()) return;
            if (Mathf.Abs(moveInput) < 0.5f)
            {
                // 入力が離れたら連続移動の待ち時間もリセットする。
                nextMoveTime = 0f;
                return;
            }

            // 押しっぱなし移動の速度制御。
            if (Time.time < nextMoveTime) return;

            // 次の1手を論理クラスに問い合わせる。
            if (!navigator.TryGetNextStep(moveInput > 0f ? 1 : -1, out var nextCell, out var nextNormal))
            {
                // まれに論理セルが見た目とズレると次の1手が見つからなくなる。
                // 現在位置から境界セルを再取得して、1回だけ探索をやり直す。
                navigator.SnapToNearestBoundary(transform.position);
                view.SnapTo(navigator.GetCellCenter(navigator.CurrentCell), navigator.GetRotation(navigator.SurfaceNormal));
                if (!navigator.TryGetNextStep(moveInput > 0f ? 1 : -1, out nextCell, out nextNormal)) return;
            }

            nextMoveTime = Time.time + moveRepeatDelay;
            StartMove(nextCell, nextNormal);
        }

        // コンポーネント停止時は見た目側のモーションだけ止めればよい。
        private void OnDisable() => view?.Stop();

        private void StartMove(Vector3Int nextCell, Vector2Int nextNormal)
        {
            // ここからアニメーション完了までは論理状態を進めない。
            isBusy = true;
            view.SetFacing(moveInput >= 0f ? 1 : -1);
            bool isConvexCornerTurn = navigator.IsConvexCornerTurn(nextCell, nextNormal);

            if (!isConvexCornerTurn)
            {
                // 普通の1手はそのまま1回の見た目補間。
                view.AnimateStep(
                    navigator.GetCellCenter(nextCell),
                    navigator.GetRotation(nextNormal),
                    moveDuration,
                    () =>
                    {
                        navigator.CommitMove(nextCell, nextNormal);
                        if (!TryStartSlipFall())
                        {
                            isBusy = false;
                        }
                    });
                return;
            }

            // 凸角だけは地面を貫通して見えないよう、中間点を1つ挟む。
            Vector3Int waypointCell = navigator.GetConvexCornerWaypoint(nextCell);
            view.AnimateConvexCorner(
                navigator.GetCellCenter(waypointCell),
                navigator.GetCellCenter(nextCell),
                navigator.GetRotation(navigator.SurfaceNormal),
                navigator.GetRotation(nextNormal),
                moveDuration,
                () =>
                {
                    navigator.CommitMove(nextCell, nextNormal);
                    if (!TryStartSlipFall())
                    {
                        isBusy = false;
                    }
                });
        }

        private bool TryStartDrill()
        {
            // まず論理クラスに「掘れるか」と「どこを通るか」を聞く。
            if (!navigator.TryBuildDrillPath(out var drillDirection, out List<Vector3Int> drillPath)) return false;

            isBusy = true;
            view.SetFacing(moveInput >= 0f ? 1 : -1);

            // 見た目は「先に回転」「次にアニメーションON」「最後に直進」。
            view.RotateThenDrill(
                navigator.GetRotation(drillDirection),
                drillRotateDuration,
                drillPath.ConvertAll(navigator.GetCellCenter),
                drillStepDuration,
                () =>
                {
                    navigator.FinishDrill(drillPath[^1], drillDirection);
                    if (!TryStartSlipFall())
                    {
                        isBusy = false;
                    }
                });
            return true;
        }

        private bool TryStartSlipFall()
        {
            // 氷の壁・天井に乗っている間は、
            // 入力より先に滑落処理を優先する。
            if (!navigator.TryBuildSlipFall(out List<Vector3Int> fallPath, out Vector3Int landingCell, out Vector2Int landingNormal))
            {
                return false;
            }

            isBusy = true;
            view.AnimateFall(
                fallPath.ConvertAll(navigator.GetCellCenter),
                navigator.GetRotation(landingNormal),
                slipFallStepDuration,
                () =>
                {
                    // 落下が終わった位置と張り付き先を論理状態へ反映する。
                    navigator.CommitMove(landingCell, landingNormal);
                    isBusy = false;
                });
            return true;
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            // PlayerInput の Unity Event から Move を受け取り、
            // 横成分だけを移動入力として保持する。
            if (context.performed)
            {
                moveInput = Mathf.Clamp(context.ReadValue<Vector2>().x, -1f, 1f);
            }
            else if (context.canceled)
            {
                moveInput = 0f;
            }
        }

        public void OnJump(InputAction.CallbackContext context)
        {
            // Jump は押された瞬間だけドリル開始要求に変換する。
            if (context.performed)
            {
                drillPressed = true;
            }
        }
    }
}
