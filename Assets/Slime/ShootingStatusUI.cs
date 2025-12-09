using UnityEngine;
using UnityEngine.UI;

namespace Slime
{
    /// <summary>
    /// 史莱姆发射状态UI - 显示目标位置粒子数量和发射状态
    /// </summary>
    public class ShootingStatusUI : MonoBehaviour
    {
        [SerializeField] private Text statusText;
        [SerializeField] private Text particleCountText;
        [SerializeField] private Image targetReticle;
        [SerializeField] private Color readyColor = Color.green;
        [SerializeField] private Color notReadyColor = Color.red;
        
        private ControllerTest _controller;
        private Slime_PBF _slimePBF;
        private Camera _mainCamera;
        
        void Start()
        {
            _controller = FindFirstObjectByType<ControllerTest>();
            _slimePBF = FindFirstObjectByType<Slime_PBF>();
            _mainCamera = Camera.main;
            
            if (targetReticle != null)
            {
                targetReticle.enabled = false;
            }
        }
        
        void Update()
        {
            if (_controller == null || _slimePBF == null)
                return;
            
            // 更新准星位置
            if (targetReticle != null && Input.GetKey(KeyCode.Mouse0))
            {
                targetReticle.enabled = true;
                UpdateReticlePosition();
            }
            else if (targetReticle != null)
            {
                targetReticle.enabled = false;
            }
        }
        
        private void UpdateReticlePosition()
        {
            if (_mainCamera == null)
                return;
            
            // 将准星放在鼠标位置
            Vector2 screenPos = Input.mousePosition;
            targetReticle.transform.position = screenPos;
        }
        
        /// <summary>
        /// 公开方法：更新状态文本
        /// </summary>
        public void UpdateStatus(string status)
        {
            if (statusText != null)
            {
                statusText.text = status;
            }
        }
        
        /// <summary>
        /// 公开方法：更新粒子数量显示
        /// </summary>
        public void UpdateParticleCount(int count, int required)
        {
            if (particleCountText != null)
            {
                particleCountText.text = $"目标粒子: {count}/{required}";
                particleCountText.color = count >= required ? readyColor : notReadyColor;
            }
        }
    }
}
