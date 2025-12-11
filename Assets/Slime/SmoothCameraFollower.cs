using UnityEngine;

namespace Slime
{
    /// <summary>
    /// 平滑第三人称摄像头跟随脚本
    /// 与角色保持相对位置，平滑跟随角色移动和旋转
    /// </summary>
    public class SmoothCameraFollower : MonoBehaviour
    {
        [Header("跟随目标")]
        [SerializeField] private Transform target; // 要跟随的目标（角色）
        [SerializeField] private Vector3 offset = new Vector3(0, 3f, -5f); // 相对于目标的位置偏移

        [Header("平滑参数")]
        [SerializeField, Range(0.1f, 20f)] private float followSpeed = 5f; // 位置跟随速度（越小越平滑）
        [SerializeField, Range(0.1f, 20f)] private float rotationSpeed = 5f; // 旋转跟随速度

        [Header("视角偏移（可选）")]
        [SerializeField] private bool useMouseLookAround = false; // 是否启用鼠标环顾
        [SerializeField, Range(1f, 180f)] private float mouseSensitivity = 2f; // 鼠标灵敏度
        [SerializeField, Range(-90f, 0f)] private float minVerticalAngle = -30f; // 最小仰角
        [SerializeField, Range(0f, 90f)] private float maxVerticalAngle = 60f; // 最大仰角

        [Header("碰撞检测")]
        [SerializeField] private bool useCollisionDetection = true; // 是否检测碰撞，避免穿墙
        [SerializeField] private float collisionRadius = 0.3f; // 碰撞球体半径
        [SerializeField] private LayerMask collisionLayer = -1; // 碰撞层
        [SerializeField] private float minCameraDistance = 0.5f; // 最近距离

        [Header("UI 层（可选）")]
        [SerializeField] private bool useWhiteUIOverlay = false; // 是否显示白色 UI 覆盖层
        [SerializeField] private Color uiOverlayColor = Color.white; // UI 覆盖层颜色
        [SerializeField] private float uiOverlayAlpha = 0.1f; // UI 覆盖层透明度

        private float _currentYaw = 0f;   // 水平旋转角度
        private float _currentPitch = 0f; // 垂直旋转角度
        private Vector3 _targetPosition;  // 目标位置
        private Quaternion _targetRotation; // 目标旋转

        private void Start()
        {
            if (target == null)
            {
                target = FindFirstObjectByType<ControllerTest>()?.transform;
                if (target == null)
                {
                    Debug.LogError("<color=red>[SmoothCameraFollower] 未找到跟随目标！</color>");
                    enabled = false;
                    return;
                }
                Debug.Log($"<color=cyan>[SmoothCameraFollower] 自动找到目标: {target.name}</color>");
            }

            // 初始化摄像头位置和旋转
            _targetPosition = target.position + offset;
            transform.position = _targetPosition;

            // 计算初始旋转角度
            Vector3 relativePos = target.position - transform.position;
            _currentYaw = Mathf.Atan2(relativePos.x, relativePos.z) * Mathf.Rad2Deg;
            _currentPitch = -Mathf.Asin(relativePos.y / relativePos.magnitude) * Mathf.Rad2Deg;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // 处理鼠标输入（如果启用）
            if (useMouseLookAround)
            {
                HandleMouseInput();
            }

            // 计算目标位置
            CalculateTargetPosition();

            // 平滑移动摄像头
            SmoothFollowPosition();

            // 平滑旋转摄像头
            SmoothFollowRotation();
        }

        /// <summary>
        /// 处理鼠标输入
        /// </summary>
        private void HandleMouseInput()
        {
            // 右键拖拽环顾视角
            if (Input.GetMouseButton(1))
            {
                float deltaX = Input.GetAxis("Mouse X") * mouseSensitivity;
                float deltaY = Input.GetAxis("Mouse Y") * mouseSensitivity;

                _currentYaw += deltaX;
                _currentPitch -= deltaY;

                // 限制垂直角度
                _currentPitch = Mathf.Clamp(_currentPitch, minVerticalAngle, maxVerticalAngle);
            }
        }

        /// <summary>
        /// 计算目标摄像头位置
        /// </summary>
        private void CalculateTargetPosition()
        {
            // 基于偏移计算初始目标位置
            Vector3 baseOffset = offset;

            // 如果启用鼠标环顾，根据旋转角度调整偏移
            if (useMouseLookAround)
            {
                baseOffset = RotateOffsetByAngles(_currentYaw, _currentPitch);
            }

            _targetPosition = target.position + baseOffset;
        }

        /// <summary>
        /// 根据旋转角度计算偏移向量
        /// </summary>
        private Vector3 RotateOffsetByAngles(float yaw, float pitch)
        {
            float distance = offset.magnitude;
            
            // 转换为弧度
            float yawRad = yaw * Mathf.Deg2Rad;
            float pitchRad = pitch * Mathf.Deg2Rad;

            // 计算新的偏移
            float horizontalDist = distance * Mathf.Cos(pitchRad);
            float x = horizontalDist * Mathf.Sin(yawRad);
            float y = distance * Mathf.Sin(pitchRad);
            float z = -horizontalDist * Mathf.Cos(yawRad);

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// 平滑移动摄像头位置
        /// </summary>
        private void SmoothFollowPosition()
        {
            Vector3 desiredPosition = _targetPosition;

            // 碰撞检测
            if (useCollisionDetection)
            {
                desiredPosition = HandleCollision(desiredPosition);
            }

            // 使用 Lerp 平滑移动
            transform.position = Vector3.Lerp(
                transform.position,
                desiredPosition,
                Time.deltaTime * followSpeed
            );
        }

        /// <summary>
        /// 平滑旋转摄像头
        /// </summary>
        private void SmoothFollowRotation()
        {
            // 计算指向目标的方向
            Vector3 directionToTarget = target.position - transform.position;
            
            if (directionToTarget.sqrMagnitude > 0.01f) // 避免向量过短
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                transform.rotation = Quaternion.Lerp(
                    transform.rotation,
                    targetRotation,
                    Time.deltaTime * rotationSpeed
                );
            }
        }

        /// <summary>
        /// 处理摄像头碰撞，防止穿墙
        /// </summary>
        private Vector3 HandleCollision(Vector3 desiredPosition)
        {
            Vector3 directionToCamera = (desiredPosition - target.position).normalized;
            float distance = Vector3.Distance(desiredPosition, target.position);

            // 从目标位置向摄像头方向执行射线检测
            if (Physics.SphereCast(
                target.position,
                collisionRadius,
                directionToCamera,
                out RaycastHit hit,
                distance,
                collisionLayer,
                QueryTriggerInteraction.Ignore
            ))
            {
                // 如果检测到碰撞，将摄像头移到碰撞点之前
                float safeDistance = Mathf.Max(hit.distance - collisionRadius, minCameraDistance);
                return target.position + directionToCamera * safeDistance;
            }

            return desiredPosition;
        }

        /// <summary>
        /// 设置跟随目标
        /// </summary>
        public void SetTarget(Transform newTarget)
        {
            if (newTarget != null)
            {
                target = newTarget;
                Debug.Log($"<color=cyan>[SmoothCameraFollower] 设置新目标: {target.name}</color>");
            }
        }

        /// <summary>
        /// 重置视角到默认位置
        /// </summary>
        public void ResetView()
        {
            _currentYaw = 0f;
            _currentPitch = 0f;
            Debug.Log("<color=cyan>[SmoothCameraFollower] 视角已重置</color>");
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || target == null)
                return;

            // 绘制目标位置
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(target.position, 0.5f);

            // 绘制摄像头目标位置
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_targetPosition, 0.3f);

            // 绘制连接线
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(target.position, _targetPosition);

            // 绘制碰撞检测球体
            if (useCollisionDetection)
            {
                Gizmos.color = new Color(1f, 0.5f, 0.5f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, collisionRadius);
            }
        }

        /// <summary>
        /// 在 GUI 中绘制白色覆盖层（用于 UI 效果）
        /// </summary>
        private void OnGUI()
        {
            if (!useWhiteUIOverlay) return;

            // 绘制半透明白色覆盖层
            GUI.color = new Color(uiOverlayColor.r, uiOverlayColor.g, uiOverlayColor.b, uiOverlayAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
