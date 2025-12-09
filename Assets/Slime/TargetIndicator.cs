using UnityEngine;

namespace Slime
{
    /// <summary>
    /// 目标指示器 - 可选的可视化组件，显示发射目标位置
    /// </summary>
    public class TargetIndicator : MonoBehaviour
    {
        [SerializeField] private float rotationSpeed = 90f;
        [SerializeField] private float pulseSpeed = 2f;
        [SerializeField] private float pulseScale = 0.2f;
        
        private Vector3 _baseScale;
        
        void Start()
        {
            _baseScale = transform.localScale;
        }
        
        void Update()
        {
            // 旋转动画
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
            
            // 脉冲缩放动画
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseScale;
            transform.localScale = _baseScale * pulse;
        }
    }
}
