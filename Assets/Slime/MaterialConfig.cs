using UnityEngine;

namespace Slime
{
    /// <summary>
    /// 单个材质的配置数据
    /// </summary>
    [System.Serializable]
    public class MaterialPropertyConfig
    {
        public string name = "Material";
        public Color baseColor = Color.white;
        public float metallic = 0f;
        public float smoothness = 0.5f;
        public Color emissionColor = Color.black;
        
        [TextArea(2, 4)]
        public string description = "";
    }

    /// <summary>
    /// 材质配置库 - 集中管理所有材质颜色和参数
    /// 自动使用 Toon Shader 覆盖原有 Shader
    /// </summary>
    [CreateAssetMenu(fileName = "MaterialConfig", menuName = "Slime/Material Config")]
    public class MaterialConfig : ScriptableObject
    {
        [SerializeField]
        public MaterialPropertyConfig[] materials = new MaterialPropertyConfig[0];

        [SerializeField, TextArea(3, 5)]
        private string notes = "材质配置说明：\n- 此配置集中管理所有Toon材质的颜色\n- 应用时会自动覆盖为 Toon Shader\n- 可在编辑器中快速修改参数";

        /// <summary>
        /// 获取指定索引的材质配置
        /// </summary>
        public MaterialPropertyConfig GetMaterial(int index)
        {
            if (index >= 0 && index < materials.Length)
            {
                return materials[index];
            }
            Debug.LogWarning($"[MaterialConfig] 材质索引 {index} 超出范围！");
            return null;
        }

        /// <summary>
        /// 根据名称获取材质配置
        /// </summary>
        public MaterialPropertyConfig GetMaterial(string materialName)
        {
            foreach (var mat in materials)
            {
                if (mat.name == materialName)
                {
                    return mat;
                }
            }
            Debug.LogWarning($"[MaterialConfig] 未找到材质: {materialName}");
            return null;
        }

        /// <summary>
        /// 创建运行时材质（使用 Toon Shader）
        /// </summary>
        public Material CreateMaterial(int index)
        {
            MaterialPropertyConfig config = GetMaterial(index);
            if (config == null) return null;

            Shader toonShader = Shader.Find("CiroContinisio/Toon");
            if (toonShader == null)
            {
                toonShader = Shader.Find("Shader Graphs/Toon");
            }
            if (toonShader == null)
            {
                toonShader = Shader.Find("Standard");
            }

            Material mat = new Material(toonShader);
            ApplyConfig(mat, config);
            return mat;
        }

        /// <summary>
        /// 创建运行时材质（使用 Toon Shader）
        /// </summary>
        public Material CreateMaterial(string materialName)
        {
            MaterialPropertyConfig config = GetMaterial(materialName);
            if (config == null) return null;

            Shader toonShader = Shader.Find("CiroContinisio/Toon");
            if (toonShader == null)
            {
                toonShader = Shader.Find("Shader Graphs/Toon");
            }
            if (toonShader == null)
            {
                toonShader = Shader.Find("Standard");
            }

            Material mat = new Material(toonShader);
            ApplyConfig(mat, config);
            return mat;
        }

        /// <summary>
        /// 应用配置到材质（覆盖为 Toon Shader 并应用颜色参数）
        /// </summary>
        public void ApplyConfig(Material material, MaterialPropertyConfig config)
        {
            if (material == null || config == null) return;

            // 获取 Toon Shader 并覆盖
            Shader toonShader = Shader.Find("CiroContinisio/Toon");
            if (toonShader == null)
            {
                toonShader = Shader.Find("Shader Graphs/Toon");
            }

            // 覆盖为 Toon Shader
            if (toonShader != null)
            {
                material.shader = toonShader;
            }

            // 尝试设置不同名称的颜色属性
            string[] colorPropertyNames = { "_Color", "_BaseColor", "Color" };
            foreach (string propName in colorPropertyNames)
            {
                if (material.HasProperty(propName))
                {
                    material.SetColor(propName, config.baseColor);
                    break;
                }
            }

            // 尝试设置金属度
            string[] metallicPropertyNames = { "_Metallic", "_Metallicness" };
            foreach (string propName in metallicPropertyNames)
            {
                if (material.HasProperty(propName))
                {
                    material.SetFloat(propName, config.metallic);
                    break;
                }
            }

            // 尝试设置光滑度
            string[] smoothnessPropertyNames = { "_Smoothness", "_Glossiness" };
            foreach (string propName in smoothnessPropertyNames)
            {
                if (material.HasProperty(propName))
                {
                    material.SetFloat(propName, config.smoothness);
                    break;
                }
            }

            // 尝试设置自发光
            string[] emissionPropertyNames = { "_EmissionColor", "_Emission" };
            foreach (string propName in emissionPropertyNames)
            {
                if (material.HasProperty(propName))
                {
                    material.SetColor(propName, config.emissionColor);
                    break;
                }
            }
        }

        /// <summary>
        /// 批量应用配置到多个材质（覆盖为 Toon Shader 并应用颜色）
        /// </summary>
        public void ApplyConfigToRenderers(Renderer[] renderers, int materialIndex)
        {
            MaterialPropertyConfig config = GetMaterial(materialIndex);
            if (config == null) return;

            int successCount = 0;
            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    ApplyConfig(mat, config);
                    successCount++;
                }
            }
            
            Debug.Log($"<color=green>[MaterialConfig] ✓ 已应用 '{config.name}' 到 {successCount} 个材质（已覆盖为 Toon Shader）</color>");
        }
    }
}
