using UnityEngine;

public class CameraController : MonoBehaviour
{
    public Transform target; // 追従するターゲット（プレイヤー）
    public Vector3 offset = new Vector3(0f, 0f, -10f); // カメラのオフセット（通常Zはマイナス）
    [Range(0.01f, 1f)]
    public float smoothTime = 0.2f; // カメラが目標地点に到達するまでの大体の時間

    private Vector3 velocity = Vector3.zero;

    private void LateUpdate()
    {
        if (target == null)
            return;

        // 目標とするカメラの位置
        Vector3 desiredPosition = target.position + offset;
        
        // SmoothDampを使用して滑らかに追従させる
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);
    }
}
