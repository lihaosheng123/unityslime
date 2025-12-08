using UnityEngine;

namespace Slime
{
    public class ScreenOrientationManager : MonoBehaviour
    {
        [SerializeField] private ScreenOrientation targetOrientation = ScreenOrientation.LandscapeLeft;

        void Awake()
        {
            // 在游戏启动时设置屏幕方向
            Screen.orientation = targetOrientation;
            
            // 锁定屏幕方向，防止自动旋转
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
        }

        // 可以通过代码调用来切换方向
        public void SetLandscape()
        {
            Screen.orientation = ScreenOrientation.LandscapeLeft;
        }

        public void SetPortrait()
        {
            Screen.orientation = ScreenOrientation.Portrait;
        }
    }
}
