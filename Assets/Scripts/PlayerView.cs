using System;
using System.Collections.Generic;
using LitMotion;
using UnityEngine;

namespace VerbGame
{
    // このクラスは「見た目」だけを担当する。
    // 位置・回転の補間、ドリルアニメーションの ON/OFF、
    // LitMotion の管理は全部ここに閉じ込める。
    public sealed class PlayerView
    {
        // 実際に動かす Transform。
        private readonly Transform target;
        // Drill アニメーション制御用。無くても落ちない前提。
        private readonly Animator animator;
        // 左右反転を適用する見た目の Transform。
        // 今回は Drill 子オブジェクトを見た目本体として使う。
        private readonly Transform visual;
        // 反転前のローカルスケール。
        private readonly Vector3 baseVisualScale;
        // 現在再生中の LitMotion。
        // 新しい演出を始める時は必ず止める。
        private MotionHandle activeMotion;

        public PlayerView(Transform target, Animator animator)
        {
            this.target = target;
            this.animator = animator;
            visual = animator != null ? animator.transform : target;
            baseVisualScale = visual.localScale;
        }

        // 初期スナップや復帰時に、補間なしでその場へ合わせる。
        public void SnapTo(Vector3 position, Quaternion rotation) => target.SetPositionAndRotation(position, rotation);

        // 左右入力に応じて見た目だけ左右反転する。
        // 地形追従の回転とは分離して、スプライトの向きだけを切り替える。
        public void SetFacing(int direction)
        {
            float sign = direction >= 0 ? -1f : 1f;
            visual.localScale = new Vector3(Mathf.Abs(baseVisualScale.x) * sign, baseVisualScale.y, baseVisualScale.z);
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
            },
            () =>
            {
                target.SetPositionAndRotation(targetPosition, Quaternion.Euler(0f, 0f, endZ));
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

        private void AnimatePosition(Vector3 targetPosition, float duration, Action onComplete)
        {
            // ドリル中は向きを固定し、位置だけを動かす。
            target.GetPositionAndRotation(out Vector3 startPos, out Quaternion fixedRotation);

            Play(duration, progress =>
            {
                target.SetPositionAndRotation(Vector3.Lerp(startPos, targetPosition, progress), fixedRotation);
            },
            () =>
            {
                target.SetPositionAndRotation(targetPosition, fixedRotation);
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
            },
            () =>
            {
                target.SetPositionAndRotation(fixedPosition, Quaternion.Euler(0f, 0f, endZ));
                onComplete?.Invoke();
            });
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
        }
    }
}
