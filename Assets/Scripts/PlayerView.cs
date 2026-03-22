using System;
using System.Collections.Generic;
using LitMotion;
using UnityEngine;
using UnityEngine.Rendering;

namespace VerbGame
{
    // このクラスは「見た目」だけを担当する。
    // 位置・回転の補間、ドリルアニメーションの ON/OFF、
    // LitMotion の管理は全部ここに閉じ込める。
    public sealed class PlayerView
    {
        // 地面タイルより前に出して、潜行位置を見失わないようにする。
        private const int GroundShadowSortingOffset = 1;

        // 実際に動かす Transform。
        private readonly Transform target;
        // Drill アニメーション制御用。無くても落ちない前提。
        private readonly Animator animator;
        // 左右反転を適用する見た目の Transform。
        // 今回は Drill 子オブジェクトを見た目本体として使う。
        private readonly Transform visual;
        // 反転前のローカルスケール。
        private readonly Vector3 baseVisualScale;
        // 影へ複製する元になる見た目スプライト。
        private readonly SpriteRenderer sourceRenderer;
        // 地中移動中だけ表示する、地面上の影スプライト。
        private readonly SpriteRenderer groundShadowRenderer;
        // 現在再生中の LitMotion。
        // 新しい演出を始める時は必ず止める。
        private MotionHandle activeMotion;

        public PlayerView(Transform target, Animator animator)
        {
            this.target = target;
            this.animator = animator;
            visual = animator != null ? animator.transform : target;
            baseVisualScale = visual.localScale;
            // 通常は Drill 子の SpriteRenderer を使うが、無ければ親へフォールバックする。
            var renderer = visual.GetComponent<SpriteRenderer>();
            sourceRenderer = renderer != null ? renderer : target.GetComponent<SpriteRenderer>();
            // 影オブジェクトはシーンへ置かず、見た目クラスの中で動的に生成する。
            groundShadowRenderer = CreateGroundShadowRenderer();
        }

        // 初期スナップや復帰時に、補間なしでその場へ合わせる。
        public void SnapTo(Vector3 position, Quaternion rotation)
        {
            target.SetPositionAndRotation(position, rotation);
            SyncGroundShadow();
        }

        // 左右入力に応じて見た目だけ左右反転する。
        // 地形追従の回転とは分離して、スプライトの向きだけを切り替える。
        public void SetFacing(int direction)
        {
            float sign = direction >= 0 ? -1f : 1f;
            visual.localScale = new Vector3(Mathf.Abs(baseVisualScale.x) * sign, baseVisualScale.y, baseVisualScale.z);
            SyncGroundShadow();
        }

        public void AnimateStep(Vector3 targetPosition, Quaternion targetRotation, float duration, Action onComplete)
        {
            // 通常の1手移動。
            // 位置は直線補間、回転は Z 角だけを補間する。
            Vector3 startPos = target.position;
            float startZ = target.eulerAngles.z;
            float endZ = targetRotation.eulerAngles.z;

            Play(duration, progress =>
            {
                float z = Mathf.LerpAngle(startZ, endZ, progress);
                target.SetPositionAndRotation(
                    Vector3.Lerp(startPos, targetPosition, progress),
                    Quaternion.Euler(0f, 0f, z));
                SyncGroundShadow();
            },
            () =>
            {
                target.SetPositionAndRotation(targetPosition, Quaternion.Euler(0f, 0f, endZ));
                SyncGroundShadow();
                onComplete?.Invoke();
            });
        }

        public void AnimateConvexCorner(Vector3 waypoint, Vector3 targetPosition, Quaternion startRotation, Quaternion targetRotation, float duration, Action onComplete)
        {
            // 凸角では中間点を挟んだ2段モーションにする。
            // 通常移動より少し大きく回り込んで見せたいので、
            // 合計で duration ではなく、各区間に duration を使う。
            AnimateStep(waypoint, startRotation, duration, () => AnimateStep(targetPosition, targetRotation, duration, onComplete));
        }

        public void RotateThenDrill(Quaternion drillRotation, float rotateDuration, List<Vector3> drillPositions, float stepDuration, Action onComplete)
        {
            // ドリルは
            // 1. その場で向きを作る
            // 2. アニメーションを ON
            // 3. 位置だけを直進
            // という順で再生する。
            AnimateRotation(drillRotation, rotateDuration, () =>
            {
                SetDrilling(true);
                PlayDrillStep(drillPositions, 0, stepDuration, onComplete);
            });
        }

        public void RotateBounceThenReturn(Quaternion drillRotation, Quaternion returnRotation, float rotateDuration, List<Vector3> drillPositions, int bounceTurnIndex, float stepDuration, Action onBounce, Action onComplete)
        {
            // 硬い壁に当たった時は、
            // いったん掘り進めてから元の向きへ戻して引き返す。
            AnimateRotation(drillRotation, rotateDuration, () =>
            {
                SetDrilling(true);
                PlayBounceDrillStep(drillPositions, 0, bounceTurnIndex, returnRotation, rotateDuration, stepDuration, onBounce, onComplete);
            });
        }

        public void AnimateFall(List<Vector3> fallPositions, Quaternion landingRotation, float stepDuration, Action onComplete)
        {
            // 氷からの滑落演出。
            // 落下セル列を順にたどり、最後だけ着地姿勢へ回す。
            if (fallPositions == null || fallPositions.Count == 0)
            {
                AnimateRotation(landingRotation, stepDuration, onComplete);
                return;
            }

            PlayFallStep(fallPositions, 0, landingRotation, stepDuration, onComplete);
        }

        // 外部から停止命令が来た時は、モーションも Drill 演出も止める。
        public void Stop()
        {
            activeMotion.TryCancel();
            SetDrilling(false);
        }

        private void PlayDrillStep(List<Vector3> drillPositions, int index, float stepDuration, Action onComplete)
        {
            // ドリル経路を1セルずつ再生する。
            AnimatePosition(drillPositions[index], stepDuration, () =>
            {
                if (index + 1 < drillPositions.Count)
                {
                    PlayDrillStep(drillPositions, index + 1, stepDuration, onComplete);
                    return;
                }

                SetDrilling(false);
                onComplete?.Invoke();
            });
        }

        private void PlayFallStep(List<Vector3> fallPositions, int index, Quaternion landingRotation, float stepDuration, Action onComplete)
        {
            // 落下は1セルずつ再生し、
            // 最終セルだけ着地先の法線へ向きを合わせる。
            bool isLastStep = index == fallPositions.Count - 1;

            AnimateStep(
                fallPositions[index],
                isLastStep ? landingRotation : target.rotation,
                stepDuration,
                () =>
                {
                    if (!isLastStep)
                    {
                        PlayFallStep(fallPositions, index + 1, landingRotation, stepDuration, onComplete);
                        return;
                    }

                    onComplete?.Invoke();
                });
        }

        private void PlayBounceDrillStep(List<Vector3> drillPositions, int index, int bounceTurnIndex, Quaternion returnRotation, float rotateDuration, float stepDuration, Action onBounce, Action onComplete)
        {
            if (drillPositions == null || index < 0 || index >= drillPositions.Count)
            {
                SetDrilling(false);
                onComplete?.Invoke();
                return;
            }

            AnimatePosition(drillPositions[index], stepDuration, () =>
            {
                if (index == bounceTurnIndex)
                {
                    // 硬い壁に当たった瞬間に向きを戻したいので、
                    // 折り返しだけは補間せず即座に回す。
                    onBounce?.Invoke();
                    SnapRotation(returnRotation);
                    PlayBounceDrillStep(drillPositions, index + 1, bounceTurnIndex, returnRotation, rotateDuration, stepDuration, onBounce, onComplete);
                    return;
                }

                if (index + 1 < drillPositions.Count)
                {
                    PlayBounceDrillStep(drillPositions, index + 1, bounceTurnIndex, returnRotation, rotateDuration, stepDuration, onBounce, onComplete);
                    return;
                }

                SetDrilling(false);
                onComplete?.Invoke();
            });
        }

        private void AnimatePosition(Vector3 targetPosition, float duration, Action onComplete)
        {
            // ドリル中は向きを固定し、位置だけを動かす。
            target.GetPositionAndRotation(out Vector3 startPos, out Quaternion fixedRotation);

            Play(duration, progress =>
            {
                target.SetPositionAndRotation(Vector3.Lerp(startPos, targetPosition, progress), fixedRotation);
                SyncGroundShadow();
            },
            () =>
            {
                target.SetPositionAndRotation(targetPosition, fixedRotation);
                SyncGroundShadow();
                onComplete?.Invoke();
            });
        }

        private void AnimateRotation(Quaternion targetRotation, float duration, Action onComplete)
        {
            // ドリル開始前のその場回転。
            // ここも Z 軸角だけを使う。
            float startZ = target.eulerAngles.z;
            float endZ = targetRotation.eulerAngles.z;
            Vector3 fixedPosition = target.position;

            Play(duration, progress =>
            {
                float z = Mathf.LerpAngle(startZ, endZ, progress);
                target.SetPositionAndRotation(fixedPosition, Quaternion.Euler(0f, 0f, z));
                SyncGroundShadow();
            },
            () =>
            {
                target.SetPositionAndRotation(fixedPosition, Quaternion.Euler(0f, 0f, endZ));
                SyncGroundShadow();
                onComplete?.Invoke();
            });
        }

        private void SnapRotation(Quaternion rotation)
        {
            // 位置は変えず、向きだけ即時反映する。
            target.SetPositionAndRotation(target.position, rotation);
            SyncGroundShadow();
        }

        private void Play(float duration, Action<float> onUpdate, Action onComplete)
        {
            // LitMotion の共通ラッパー。
            // 毎回 0→1 の progress を流し、外側で更新処理を渡す。
            activeMotion.TryCancel();
            activeMotion = LMotion.Create(0f, 1f, duration)
                .WithOnComplete(onComplete)
                .Bind(onUpdate.Invoke);
        }

        private void SetDrilling(bool value)
        {
            // Animator が無い構成でも null なら何もしない。
            if (animator != null) animator.SetBool("Drilling", value);
            // 地中にいる間だけ影を表示する。
            if (groundShadowRenderer != null) groundShadowRenderer.enabled = value;
            SyncGroundShadow();
        }

        private SpriteRenderer CreateGroundShadowRenderer()
        {
            // 元スプライトが無い構成では影も作れない。
            if (sourceRenderer == null) return null;

            var shadowObject = new GameObject("GroundShadow");
            shadowObject.transform.SetParent(target, false);

            var renderer = shadowObject.AddComponent<SpriteRenderer>();
            renderer.enabled = false;
            // プレイヤー本体ではなく、地面への投影に見せるため黒半透明にする。
            renderer.color = new Color(0f, 0f, 0f, 0.35f);
            renderer.maskInteraction = sourceRenderer.maskInteraction;
            renderer.drawMode = sourceRenderer.drawMode;
            renderer.size = sourceRenderer.size;
            renderer.sortingLayerID = sourceRenderer.sortingLayerID;
            renderer.sortingOrder = sourceRenderer.sortingOrder + GroundShadowSortingOffset;
            renderer.spriteSortPoint = sourceRenderer.spriteSortPoint;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = false;
            return renderer;
        }

        private void SyncGroundShadow()
        {
            // 通常移動中は非表示だが、姿勢やアニメフレームの同期はいつでもできるようにする。
            if (groundShadowRenderer == null || sourceRenderer == null) return;

            Transform shadowTransform = groundShadowRenderer.transform;
            // 影は見た目本体と同じローカル姿勢を取る。
            shadowTransform.SetLocalPositionAndRotation(visual.localPosition, visual.localRotation);
            shadowTransform.localScale = visual.localScale;

            // 現在表示中のスプライトをそのまま複製して影として使う。
            groundShadowRenderer.sprite = sourceRenderer.sprite;
            groundShadowRenderer.flipX = sourceRenderer.flipX;
            groundShadowRenderer.flipY = sourceRenderer.flipY;
            groundShadowRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
            groundShadowRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            groundShadowRenderer.sortingOrder = sourceRenderer.sortingOrder + GroundShadowSortingOffset;
            groundShadowRenderer.size = sourceRenderer.size;
        }
    }
}
