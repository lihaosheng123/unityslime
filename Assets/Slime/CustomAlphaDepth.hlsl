// Custom Function Node: CustomAlphaDepthWrite
// 在 Shader Graph 中创建 Custom Function 节点
// 用于在半透明模式下控制深度写入

void CustomAlphaDepthWrite_float(
    float Alpha,           // 输入：透明度
    float DepthThreshold,  // 输入：深度写入阈值（0-1）
    out float OutAlpha,    // 输出：调整后的透明度
    out float OutDepth     // 输出：深度值
)
{
    OutAlpha = Alpha;
    
    // 当透明度高于阈值时，写入深度
    // 这样可以确保主要可见部分参与深度测试
    OutDepth = Alpha > DepthThreshold ? 1.0 : 0.0;
}

// 使用方法：
// 1. 在 Shader Graph 中创建 Custom Function 节点
// 2. Mode: String
// 3. 将上面的代码粘贴到 Body 中
// 4. 添加输入输出端口（如上所示）
// 5. 将 OutDepth 连接到一个自定义的深度写入逻辑
