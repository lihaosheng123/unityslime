using UnityEngine;
using UnityEngine.Profiling;

namespace Slime
{
    /// <summary>
    /// 调试用 UI：显示 FPS、内存使用情况。
    /// 挂载到场景中任意 GameObject 即可。
    /// </summary>
    public class DebugStatsUI : MonoBehaviour
    {
        [Header("显示设置")]
        [SerializeField] private bool showFPS = true;
        [SerializeField] private bool showMemory = true;
        [SerializeField] private float updateInterval = 0.5f; // 刷新间隔(秒)

        [Header("样式")]
        [SerializeField] private int fontSize = 20;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color backgroundColor = new Color(0, 0, 0, 0.6f);

        private float _fps;
        private float _deltaTime;
        private float _timer;
        private int _frameCount;

        // 内存数据 (MB)
        private float _totalAllocatedMemory;
        private float _totalReservedMemory;
        private float _monoHeapSize;
        private float _monoUsedSize;
        private float _gfxMemory;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private Texture2D _bgTexture; // 缓存背景贴图避免重复创建

        private void Awake()
        {
            // 只创建一次背景贴图
            _bgTexture = MakeTexture(2, 2, backgroundColor);
        }

        private void Start()
        {
            // 初始化 GUI 样式
            _boxStyle = new GUIStyle();
            _boxStyle.normal.background = _bgTexture;

            _labelStyle = new GUIStyle();
            _labelStyle.fontSize = fontSize;
            _labelStyle.normal.textColor = textColor;
            _labelStyle.padding = new RectOffset(5, 5, 2, 2);
        }

        private void OnDestroy()
        {
            // 释放贴图
            if (_bgTexture != null)
            {
                Destroy(_bgTexture);
                _bgTexture = null;
            }
        }

        private void Update()
        {
            // 累计帧
            _frameCount++;
            _deltaTime += Time.unscaledDeltaTime;
            _timer += Time.unscaledDeltaTime;

            if (_timer >= updateInterval)
            {
                // 计算 FPS
                _fps = _frameCount / _deltaTime;
                _frameCount = 0;
                _deltaTime = 0f;
                _timer = 0f;

                // 更新内存数据
                UpdateMemoryStats();
            }
        }

        private void UpdateMemoryStats()
        {
            // Unity Profiler 内存 (需 Development Build 或编辑器)
            _totalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
            _totalReservedMemory = Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f);

            // Mono 堆 (C# 托管内存)
            _monoHeapSize = Profiler.GetMonoHeapSizeLong() / (1024f * 1024f);
            _monoUsedSize = Profiler.GetMonoUsedSizeLong() / (1024f * 1024f);

            // 显存 (仅编辑器/Dev Build 有效)
            _gfxMemory = Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024f * 1024f);
        }

        private void OnGUI()
        {
            if (_boxStyle == null || _labelStyle == null)
                return;

            float lineHeight = fontSize + 4;
            float boxWidth = 280;
            float boxHeight = 0;

            // 计算高度
            if (showFPS) boxHeight += lineHeight;
            if (showMemory) boxHeight += lineHeight * 5;
            boxHeight += 10; // padding

            Rect boxRect = new Rect(10, 10, boxWidth, boxHeight);
            GUI.Box(boxRect, GUIContent.none, _boxStyle);

            float y = 15;

            if (showFPS)
            {
                GUI.Label(new Rect(15, y, boxWidth, lineHeight),
                    $"FPS: {_fps:F1}  ({1000f / Mathf.Max(_fps, 0.001f):F2} ms)",
                    _labelStyle);
                y += lineHeight;
            }

            if (showMemory)
            {
                GUI.Label(new Rect(15, y, boxWidth, lineHeight),
                    $"Allocated: {_totalAllocatedMemory:F1} MB",
                    _labelStyle);
                y += lineHeight;

                GUI.Label(new Rect(15, y, boxWidth, lineHeight),
                    $"Reserved:  {_totalReservedMemory:F1} MB",
                    _labelStyle);
                y += lineHeight;

                GUI.Label(new Rect(15, y, boxWidth, lineHeight),
                    $"Mono Heap: {_monoHeapSize:F1} MB",
                    _labelStyle);
                y += lineHeight;

                GUI.Label(new Rect(15, y, boxWidth, lineHeight),
                    $"Mono Used: {_monoUsedSize:F1} MB",
                    _labelStyle);
                y += lineHeight;

                GUI.Label(new Rect(15, y, boxWidth, lineHeight),
                    $"GFX Mem:   {_gfxMemory:F1} MB",
                    _labelStyle);
            }
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
