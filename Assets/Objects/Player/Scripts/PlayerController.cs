using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    [RequireComponent(typeof(AudioSource))]
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

        [Header("Audio")]
        [SerializeField] private AudioClip drillClip;
        [SerializeField] private AudioClip fallClip;
        [SerializeField] private AudioClip hardWallClip;

        [Header("Goal")]
        // クリア表示を出してから次シーンへ進むまでの待ち時間。
        [SerializeField] private float clearDisplayDuration = 1.5f;
        // シーン上の Clear オブジェクト。表示は Active 切り替えで行う。
        [SerializeField] private GameObject clearObject;

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
        // 効果音専用。PlayOneShot で重ねる。
        private AudioSource sfxSource;
        // クリア演出中は入力も遷移も1回だけにする。
        private bool isClearingStage;

        private void Awake()
        {
            // ロジック担当と見た目担当をここで組み立てる。
            navigator = new PlayerGridNavigator(grid, groundTilemap, wallPanelCatalog);
            view = new PlayerView(transform, GetComponentInChildren<Animator>());
            EnsureAudioSource();
            EnsureClearObject();

            RespawnToSpawn();
        }

        private void Update()
        {
            // 参照不足、または何かの演出中なら新しい操作は受け付けない。
            if (isBusy || isClearingStage || grid == null || groundTilemap == null || navigator == null || view == null) return;

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
                        FinishMovementStep();
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
                    FinishMovementStep();
                });
        }

        private bool TryStartDrill()
        {
            // まず論理クラスに「掘れるか」と「どこを通るか」を聞く。
            if (!navigator.TryBuildDrillPath(out var drillDirection, out List<Vector3Int> drillPath, out Vector3Int endCell, out Vector2Int endNormal, out bool bouncedByHardWall, out List<int> drillTurnIndices, out List<Vector2Int> drillTurnDirections)) return false;

            isBusy = true;
            view.SetFacing(moveInput >= 0f ? 1 : -1);

            List<Vector3> drillPositions = drillPath.ConvertAll(navigator.GetCellCenter);
            List<Quaternion> drillTurnRotations = drillTurnDirections.ConvertAll(navigator.GetRotation);

            // 見た目は「先に回転」「次にアニメーションON」「最後に経路どおり進む」。
            PlaySfx(drillClip);
            view.RotateThenDrillWithTurns(
                navigator.GetRotation(drillDirection),
                drillRotateDuration,
                drillPositions,
                drillTurnIndices,
                drillTurnRotations,
                drillStepDuration,
                bouncedByHardWall ? () => PlaySfx(hardWallClip) : null,
                () =>
                {
                    // 通常の壁なら出口へ抜け、
                    // 反射した場合は最終的な経路結果をそのまま確定する。
                    navigator.FinishDrill(endCell, endNormal);
                    FinishMovementStep();
                });
            return true;
        }

        private bool TryStartSlipFall()
        {
            // 氷の壁・天井に乗っている間は、
            // 入力より先に滑落処理を優先する。
            if (navigator == null || view == null) return false;

            if (!navigator.TryBuildSlipFall(out List<Vector3Int> fallPath, out Vector3Int landingCell, out Vector2Int landingNormal))
            {
                return false;
            }

            isBusy = true;
            PlaySfx(fallClip);
            view.AnimateFall(
                fallPath.ConvertAll(navigator.GetCellCenter),
                navigator.GetRotation(landingNormal),
                slipFallStepDuration,
                () =>
                {
                    // 落下が終わった位置と張り付き先を論理状態へ反映する。
                    navigator.CommitMove(landingCell, landingNormal);
                    if (!TryHandleStageClear())
                    {
                        isBusy = false;
                    }
                });
            return true;
        }

        private void FinishMovementStep()
        {
            // 1手ぶんの演出が終わった後は、
            // 滑落、クリア判定、入力再開の順で後処理する。
            if (TryStartSlipFall()) return;
            if (TryHandleStageClear()) return;
            isBusy = false;
        }

        private bool TryHandleStageClear()
        {
            // 現在触れている面がチェックポイントなら、
            // ここで操作を止めてクリア演出へ入る。
            if (isClearingStage || navigator == null || !navigator.IsTouchingCheckpoint()) return false;

            isClearingStage = true;
            moveInput = 0f;
            drillPressed = false;
            nextMoveTime = 0f;
            SetClearVisible(true);
            StartCoroutine(ShowClearAndLoadNextScene());
            return true;
        }

        private IEnumerator ShowClearAndLoadNextScene()
        {
            // 先に Clear 画像を見せてからシーンを切り替える。
            yield return new WaitForSeconds(clearDisplayDuration);

            if (TryResolveNextScene(out string nextSceneName))
            {
                SceneManager.LoadScene(nextSceneName);
                yield break;
            }

            Debug.LogWarning($"Next scene was not found from '{SceneManager.GetActiveScene().name}'.");
            SetClearVisible(false);
            isBusy = false;
            isClearingStage = false;
        }

        private bool TryResolveNextScene(out string nextSceneName)
        {
            // まず scene1 -> scene2 のような命名規則で探し、
            // 無ければ Build Settings の次インデックスを使う。
            Scene currentScene = SceneManager.GetActiveScene();
            nextSceneName = string.Empty;

            if (TryBuildIncrementedSceneName(currentScene.name, out string incrementedName) &&
                HasSceneInBuildSettings(incrementedName))
            {
                nextSceneName = incrementedName;
                return true;
            }

            int nextBuildIndex = currentScene.buildIndex + 1;
            if (nextBuildIndex >= 0 && nextBuildIndex < SceneManager.sceneCountInBuildSettings)
            {
                string nextScenePath = SceneUtility.GetScenePathByBuildIndex(nextBuildIndex);
                nextSceneName = System.IO.Path.GetFileNameWithoutExtension(nextScenePath);
                return !string.IsNullOrEmpty(nextSceneName);
            }

            return false;
        }

        private bool HasSceneInBuildSettings(string sceneName)
        {
            // Build Settings にその名前のシーンが入っているかを確認する。
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                if (System.IO.Path.GetFileNameWithoutExtension(scenePath) == sceneName)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryBuildIncrementedSceneName(string sceneName, out string incrementedName)
        {
            // 末尾の数字だけを 1 増やして次シーン名を組み立てる。
            incrementedName = string.Empty;
            if (string.IsNullOrEmpty(sceneName)) return false;

            int suffixStart = sceneName.Length;
            while (suffixStart > 0 && char.IsDigit(sceneName[suffixStart - 1]))
            {
                suffixStart--;
            }

            if (suffixStart == sceneName.Length) return false;

            string prefix = sceneName[..suffixStart];
            string suffix = sceneName[suffixStart..];
            if (!int.TryParse(suffix, out int sceneNumber)) return false;

            incrementedName = prefix + (sceneNumber + 1);
            return true;
        }

        private void EnsureClearObject()
        {
            SetClearVisible(false);
        }

        private void SetClearVisible(bool isVisible)
        {
            // 表示そのものは GameObject の Active 切り替えだけで行う。
            if (clearObject == null) return;
            clearObject.SetActive(isVisible);
        }

        private void EnsureAudioSource()
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
            }

            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
        }

        private void PlaySfx(AudioClip clip)
        {
            if (sfxSource == null || clip == null) return;
            sfxSource.PlayOneShot(clip);
        }

        public bool RespawnToSpawn()
        {
            if (navigator == null || view == null) return false;

            view.Stop();
            isBusy = false;
            isClearingStage = false;
            moveInput = 0f;
            drillPressed = false;
            nextMoveTime = 0f;
            SetClearVisible(false);

            // Spawn タイルがあればその脇へ出し、
            // 無ければ従来どおり最寄り境界へスナップする。
            if (navigator.TryFindSpawnBoundary(out var spawnCell, out var spawnNormal))
            {
                navigator.CommitMove(spawnCell, spawnNormal);
            }
            else
            {
                navigator.SnapToNearestBoundary(transform.position);
            }

            view.SnapTo(navigator.GetCellCenter(navigator.CurrentCell), navigator.GetRotation(navigator.SurfaceNormal));
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
