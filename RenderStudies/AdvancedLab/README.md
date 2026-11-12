# 实验室离线混合渲染

本实验独立于 PC 模拟场景，使用已有 Hunyuan3D 器皿模型搭建较复杂的实验室，只输出静态图像。不会修改 Unity 主场景、材质或 PC 构建。

## 图片

最终图片位于 `images`：

| 视角 | 级联阴影＋光线追踪 | 同视角白模 |
| --- | --- | --- |
| 实验室全景 | `view01_hybrid.png` | `view01_white.png` |
| 器皿近景 | `view02_hybrid.png` | `view02_white.png` |

每组采用完全相同的相机变换、焦距、分辨率和几何体。白模采用 EEVEE 光栅化与简单法线明暗、单一中性色，不计算投影、反射、折射和光追；它仍是三维模型的成像，并不是“完全不进行渲染”。

## 如何组合两种算法

`scripts/render_lab.py` 实现四级联阴影贴图，并把其结果加入 Cycles 材质，不把光线追踪产生的阴影冒充级联阴影。

1. 根据主相机将视锥深度划分为 `[0.1, 4, 7, 12, 24] m` 四段。
2. 为每段计算八个视锥角点，变换到太阳光坐标系，拟合正交阴影相机。将投影中心对齐到纹素，减少阴影不稳定。
3. EEVEE 光栅化生成四张 32 位浮点 EXR 深度贴图，每张正式分辨率为 2048×2048。保存的是归一化光空间线性深度，不是经过色调映射的颜色。
4. 材质节点将世界坐标投影到对应阴影贴图，与保存的深度比较；使用四点 PCF、随表面斜率变化的深度偏移，以及级联末端 10% 范围的过渡混合。
5. 将遮挡结果乘以法线与太阳方向的余弦、漫反射颜色和太阳强度，得到太阳光的漫反射出射辐亮度，并添加到表面 BSDF。Cycles 追踪间接反射，以及区域光／环境光引起的反射、折射和玻璃照明。

这是一种用于静态展示的自定义混合方法：**太阳漫反射遮挡来自真正的级联深度贴图，反射／折射和光传输来自 Cycles 路径追踪**。每个视角的四张深度图和相机参数保存在 `evidence`，可审查实际算法输入。正式输出前还对地面／桌面采样点做独立 BVH 射线检测，与深度贴图的遮挡分类对照；报告为 `view01_shadow_validation.json` 和 `view02_shadow_validation.json`。边缘 PCF 与级联混合区域不纳入该分类检查。

## 设置与边界

- Blender 4.2，Cycles CPU，8 个线程；正式图 1800×1200、最多 32 samples，开启自适应采样和降噪。
- 最多 10 次路径反弹，透射 8 次，漫反射和光泽反射各 4 次。
- 独立场景包含实验岛台、柜体、药品架、窗格、遮阳构件、仪器、支架及修复后的器皿。场景设施采用程序建模，不宣称由混元生成。
- 烧杯和锥形瓶来自 `SourceAssets/Models` 中已修复的 `.blend`，原始文件不改动。液体是独立场景对象，不是器皿内部误建的隔板。
- CSM 深度图排除玻璃及液体，避免把透明器皿算成完全不透光的挡板。其区域光／环境光传输由 Cycles 计算；没有实现太阳穿过玻璃产生的完整焦散、太阳圆盘的镜面贡献。
- 间接太阳照明也使用当前相机的阴影缓存，视锥外或超过阴影距离的结果是近似。该方案不是完整的物理无偏太阳光照解法。
- 阴影深度比较采用 0.025 m 基础偏移与斜率偏移，以抑制自阴影条纹；这会牺牲部分太阳接触阴影精度。区域光的接触阴影仍由光追计算。
- 不依赖显卡硬件光追，也不宣称已经给 Unity 实时场景加入了光追。

## 复现

在项目根目录执行（Blender 安装路径可调整）：

```powershell
& 'D:\Blender 4.2\blender.exe' -b -t 8 --python RenderStudies/AdvancedLab/scripts/render_lab.py
```

加 `-- --preview` 输出低分辨率预览；加 `-- --view 1` 或 `-- --view 2` 仅重渲染对应视角。两份正式可编辑场景保存到 `scene/view01_hybrid.blend`、`scene/view02_hybrid.blend`，内部打包各自的阴影贴图。调整相机或几何后需要重新运行脚本生成匹配的级联贴图，不能只移动已保存场景的相机并沿用旧缓存。

## 参考

- [Unity 阴影级联说明](https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline/urp-reference/universalrp-asset)：按观察距离分配阴影贴图精度的概念参考；本实验没有使用 Unity URP 来生成这些图片。
- [Blender Cycles 介绍](https://docs.blender.org/manual/en/2.80/render/cycles/introduction.html)：路径追踪、反射和透射光线。
- [Blender 4.2 采样](https://docs.blender.org/manual/en/4.2/render/cycles/render_settings/sampling.html)。
