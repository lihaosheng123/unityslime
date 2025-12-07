# Shader Graph 高光问题详细修复

## 🔍 问题诊断

从你的截图可以看到：
- 史莱姆主体是**青绿色** ✅
- 但边缘有一条**白色亮线** ❌
- 这是**高光（Specular/Smoothness）**造成的

---

## 🎯 三种修复方案

### 方案 1: 直接在材质中修改（最快）⭐⭐⭐

#### 操作步骤
1. 选中史莱姆使用的材质
2. 在 Inspector 中找到以下参数：
   - **Smoothness** 或 **Glossiness**
   - **Metallic** 或 **Metalness**

3. 修改数值：

```yaml
# 当前（可能）
Smoothness: 0.8 - 1.0
Metallic: 0.5 - 1.0

# 改为
Smoothness: 0.2 - 0.3  # ← 关键
Metallic: 0.1 - 0.2    # ← 关键
```

4. 实时查看效果，调到满意为止

---

### 方案 2: 在 Shader Graph 中修改节点（推荐）⭐⭐

#### 步骤 1: 打开 Shader Graph
双击 `slimeMat.shadergraph`

#### 步骤 2: 找到 Fragment 节点
- 在编辑器右侧找到 **Master Stack** 或 **Fragment** 输出节点
- 这是最终输出的节点

#### 步骤 3: 找到 Smoothness 输入
- Fragment 节点上有多个输入
- 找到 **Smoothness** 输入槽

#### 步骤 4: 修改连接
有以下几种情况：

##### 情况 A: 直接连了一个 Property 节点
```
[Smoothness Property] ──> [Fragment.Smoothness]
```
**修改方法：**
1. 点击 Property 节点
2. 在 Node Settings 中修改 Default Value
3. 改为 0.2 - 0.3

##### 情况 B: 连了一个 Float 节点
```
[Float: 0.9] ──> [Fragment.Smoothness]
```
**修改方法：**
1. 双击 Float 节点
2. 改为 0.2 或 0.3

##### 情况 C: 没有连接（使用默认值）
**修改方法：**
1. 在节点库中搜索 **Float** 节点
2. 添加一个 Float 节点，值设为 0.3
3. 连接到 Fragment.Smoothness

#### 步骤 5: 同样处理 Metallic
找到 **Metallic** 输入，设为 0.1 - 0.2

#### 步骤 6: 保存测试
- **Ctrl+S** 保存
- 等待编译
- 运行游戏测试

---

### 方案 3: 添加深度感知高光（高级）⭐

这个方案会让高光只在可见部分显示，被遮挡时自动隐藏。

#### 需要启用 URP 深度纹理
1. 打开 **Edit** → **Project Settings** → **Graphics**
2. 选择 URP 资产（Universal Render Pipeline Asset）
3. 找到 **Depth Texture** 选项
4. 勾选 **Enabled**

#### Shader Graph 节点设置

##### 节点列表
1. **Scene Depth** 节点
2. **Screen Position** 节点（UV 模式）
3. **Split** 节点（提取 W 分量）
4. **Subtract** 节点
5. **Step** 节点
6. **Multiply** 节点

##### 节点连接
```
                    ┌─[Scene Depth]
                    │
[Screen Position]───┼─[Split].W
                    │
                    └─> [Subtract] ─> [Step(0.01)] ─┐
                                                     ├─> [Multiply] ─> [Smoothness]
[Original Smoothness] ───────────────────────────────┘
```

##### 详细说明
1. **Scene Depth**: 获取场景深度（其他物体的深度）
2. **Screen Position + Split.W**: 获取当前片元的深度
3. **Subtract**: 计算深度差
4. **Step**: 如果被遮挡（深度差 < 0），输出 0；否则输出 1
5. **Multiply**: 被遮挡时，Smoothness 自动变为 0

##### 效果
- ✅ 史莱姆可见时：正常高光
- ✅ 史莱姆被遮挡时：**高光自动消失，无白线**

---

## 🎨 推荐参数组合

### 组合 1: 柔和半透明（推荐）⭐⭐⭐
```yaml
Base Color: RGB(0.3, 1.0, 0.85), A(0.6)
Metallic: 0.15
Smoothness: 0.25
Blending Mode: Premultiply
```
**效果**: 青绿色柔和半透明，无白线

---

### 组合 2: 完全无高光
```yaml
Base Color: RGB(0.35, 1.0, 0.9), A(0.65)
Metallic: 0
Smoothness: 0
Blending Mode: Premultiply
```
**效果**: 纯净半透明，绝对无白线，但可能显得暗淡

---

### 组合 3: 保留少量光泽
```yaml
Base Color: RGB(0.3, 0.95, 0.85), A(0.6)
Metallic: 0.3
Smoothness: 0.4
Blending Mode: Premultiply
```
**效果**: 有光泽感，但可能还有轻微白线

---

## 🔬 深入理解

### 为什么高光会穿透？

#### 渲染顺序
```
1. 不透明物体（墙）先渲染
2. 透明物体（史莱姆）后渲染
3. 史莱姆的 Shader 计算高光
4. 高光是白色 (1, 1, 1)
5. 白色高光直接混合到画面上
   → 产生白线
```

#### 混合公式
```hlsl
// 即使设置了 Premultiply
Color = BaseColor * Alpha + Specular

// 当 Specular = (1, 1, 1)（白色）
// 结果 = 青绿色 * 0.6 + 白色
//      = 青绿色 + 白色
//      = 浅色或白色
```

### 为什么降低 Smoothness 有效？

```hlsl
// 高光强度公式
Specular = pow(NdotH, Smoothness * 128) * Intensity

// 当 Smoothness = 1.0
// Specular = pow(NdotH, 128)  // 非常集中的高光

// 当 Smoothness = 0.3
// Specular = pow(NdotH, 38)  // 分散的高光

// 当 Smoothness = 0
// Specular = 0  // 无高光
```

**降低 Smoothness** → 高光更分散 → 强度降低 → 白色不明显

---

## 🛠️ Shader Graph 可视化调整

### 添加实时预览参数

#### 步骤 1: 创建 Property
1. 在 Shader Graph 的 **Blackboard** 面板
2. 点击 **+** 添加新 Property
3. 类型选择 **Float**
4. 命名为 `_Smoothness`
5. 设置 **Default Value** = 0.3
6. 设置 **Range** = 0 到 1

#### 步骤 2: 连接到 Fragment
```
[_Smoothness Property] ──> [Fragment.Smoothness]
```

#### 步骤 3: 实时调整
现在可以在材质 Inspector 中实时调整 `_Smoothness` 滑块，立即看到效果！

---

## 📊 不同 Smoothness 的效果

| Smoothness | 高光类型 | 白线问题 | 视觉效果 |
|-----------|---------|---------|---------|
| 0.0 | 无高光 | ✅ 完全无 | 暗淡 |
| 0.2 | 极柔和 | ✅ 基本无 | 自然 ⭐ |
| 0.4 | 柔和 | ⚠️ 轻微 | 有光泽 |
| 0.6 | 明显 | ❌ 明显 | 闪亮 |
| 0.9 | 镜面 | ❌ 严重 | 像金属 |

---

## 🎯 快速修复流程图

```
史莱姆有白线？
│
├─ 是 → 材质中找到 Smoothness
│      │
│      ├─ 找到了 → 改为 0.2 - 0.3 → 测试
│      │          │
│      │          ├─ 白线消失 → ✅ 成功
│      │          └─ 还有白线 → 继续降低到 0.1
│      │
│      └─ 没找到 → 打开 Shader Graph
│                 │
│                 └─ Fragment.Smoothness → 添加 Float(0.3)
│
└─ 否 → 🎉 已解决
```

---

## 🚀 终极解决方案（组合拳）

### 同时应用所有修复

#### 1. Shader Graph 设置
```yaml
Surface Type: Transparent
Blending Mode: Premultiply  # 避免白色混合
```

#### 2. Fragment 输入
```yaml
Smoothness: 0.25  # 降低高光
Metallic: 0.15    # 降低金属度
```

#### 3. 材质参数
```yaml
Base Color: RGB(0.3, 1.0, 0.85), A(0.6)
```

#### 4. 深度感知（可选）
添加深度检测节点，被遮挡时自动禁用高光

---

## 💡 Pro Tips

### Tip 1: 使用 Fresnel 替代 Specular
```
Fresnel 只在边缘发光
不会在中心产生白色高光
视觉效果更自然
```

### Tip 2: 分离可见和遮挡时的表现
```
使用深度检测
可见时: Smoothness = 0.5
被遮挡时: Smoothness = 0
```

### Tip 3: 使用 Emission 增加亮度
```
不要依赖高光来提亮
使用淡青色 Emission
效果更好且不会有白线
```

---

## 📸 修复前后对比

### 修复前
- 史莱姆主体：✅ 青绿色
- 墙后边缘：❌ **白色亮线**
- Smoothness：0.9
- Metallic：0.8

### 修复后
- 史莱姆主体：✅ 青绿色
- 墙后边缘：✅ **青绿色，无白线**
- Smoothness：0.25
- Metallic：0.15

---

## 🎉 总结

### 白线的根本原因
**高光（Specular）计算不考虑深度遮挡**

### 最简单的修复
```
降低 Smoothness: 0.2 - 0.3
降低 Metallic: 0.1 - 0.2
```

### 最完美的修复
```
1. Blending Mode = Premultiply
2. Smoothness = 0.25
3. Metallic = 0.15
4. 添加深度感知（可选）
```

### 修复时间
- 方案 1（材质）: 10 秒
- 方案 2（Shader Graph）: 30 秒
- 方案 3（深度感知）: 5 分钟

---

**立即修改 Smoothness 参数，白线应该会立刻消失！** 🎮✨
