using UnityEngine;

// カメラの追従と、背景パララックスをまとめて担当する。
// 通常はプレイヤーへ追従し、編集モード中だけ手動パンへ切り替えられる。
public class CameraController : MonoBehaviour
{
    public Transform target; // 追従するターゲット（プレイヤー）
    public Vector3 offset = new Vector3(0f, 0f, -10f); // カメラのオフセット（通常Zはマイナス）
    [Range(0.01f, 1f)]
    public float smoothTime = 0.2f; // カメラが目標地点に到達するまでの大体の時間
    [Header("Parallax")]
    public Transform backgroundGrid; // 背景グリッド。未設定時は名前から探す
    [Range(0f, 1f)]
    public float backgroundFollowFactor = 0.35f; // カメラより遅く追従させる割合

    private Vector3 velocity = Vector3.zero;
    private Vector3 initialCameraPosition;
    private Vector3 initialBackgroundPosition;
    // false の間は追従を止めて、外部から位置を直接動かす。
    private bool followEnabled = true;

    // 初期位置を覚えて、背景の相対移動計算に使う。
    private void Awake()
    {
        initialCameraPosition = transform.position;

        if (backgroundGrid != null)
        {
            initialBackgroundPosition = backgroundGrid.position;
        }
    }

    // 通常時は target へ滑らかに追従する。
    private void LateUpdate()
    {
        if (!followEnabled || target == null)
            return;

        // 目標とするカメラの位置
        Vector3 desiredPosition = target.position + offset;

        // SmoothDampを使用して滑らかに追従させる

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);

        UpdateBackgroundParallax();
    }

    // 追従の ON/OFF を切り替える。
    public void SetFollowEnabled(bool enabled)
    {
        followEnabled = enabled;
        velocity = Vector3.zero;
    }

    // 編集モードのパン用。追従を止めて任意位置へ即座に動かす。
    public void SetManualPosition(Vector3 position)
    {
        followEnabled = false;
        velocity = Vector3.zero;
        transform.position = position;
        UpdateBackgroundParallax();
    }

    // 手動パン後に、プレイヤー追従へ戻す。
    public void ResumeFollowToTarget()
    {
        if (target == null)
            return;

        followEnabled = true;
        velocity = Vector3.zero;
        transform.position = target.position + offset;
        UpdateBackgroundParallax();
    }

    // カメラ移動量に応じて背景を遅れて動かし、奥行き感を出す。
    private void UpdateBackgroundParallax()
    {
        if (backgroundGrid == null)
            return;

        Vector3 cameraDelta = transform.position - initialCameraPosition;
        Vector3 backgroundPosition = initialBackgroundPosition;
        backgroundPosition.x += cameraDelta.x * backgroundFollowFactor;
        backgroundPosition.y += cameraDelta.y * backgroundFollowFactor;
        backgroundGrid.position = backgroundPosition;
    }
}
