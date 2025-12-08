using UnityEngine;

namespace Slime
{
    public class CameraFollow : MonoBehaviour
    {
        [Header("跟随目标")]
        [SerializeField] private Transform target;
        
        [Header("跟随设置")]
        [SerializeField] private Vector3 offset = new Vector3(0, 5, -10);
        [SerializeField] private float smoothSpeed = 5f;
        [SerializeField] private bool lookAtTarget = true;
        
        [Header("高度限制")]
        [SerializeField] private bool limitHeight = false;
        [SerializeField] private float minHeight = 1f;
        [SerializeField] private float maxHeight = 20f;

        private Vector3 _velocity = Vector3.zero;

        void Start()
        {
            // 如果没有手动分配目标，尝试查找史莱姆
            if (target == null)
            {
                var controller = FindFirstObjectByType<ControllerTest>();
                if (controller != null)
                {
                    target = controller.transform;
                }
            }
        }

        void LateUpdate()
        {
            if (target == null) return;

            // 计算目标位置
            Vector3 desiredPosition = target.position + offset;
            
            // 限制高度
            if (limitHeight)
            {
                desiredPosition.y = Mathf.Clamp(desiredPosition.y, minHeight, maxHeight);
            }

            // 平滑跟随
            Vector3 smoothedPosition = Vector3.SmoothDamp(transform.position, desiredPosition, ref _velocity, 1f / smoothSpeed);
            transform.position = smoothedPosition;

            // 看向目标
            if (lookAtTarget)
            {
                transform.LookAt(target);
            }
        }

        // 设置跟随目标（可在运行时调用）
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        // 设置偏移（可在运行时调用）
        public void SetOffset(Vector3 newOffset)
        {
            offset = newOffset;
        }
    }
}
