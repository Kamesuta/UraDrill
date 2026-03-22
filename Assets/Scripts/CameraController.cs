using UnityEngine;

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

    private void Awake()
    {
        initialCameraPosition = transform.position;

        if (backgroundGrid != null)
        {
            initialBackgroundPosition = backgroundGrid.position;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        // 目標とするカメラの位置
        Vector3 desiredPosition = target.position + offset;

        // SmoothDampを使用して滑らかに追従させる

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);

        UpdateBackgroundParallax();
    }

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
