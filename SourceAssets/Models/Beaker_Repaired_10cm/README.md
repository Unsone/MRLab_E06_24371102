# 烧杯 Blender 修复版

**已被修正版本替代**：此版仍包含混元误建的水面隔板，不能作为最终空烧杯。请使用 `../Beaker_Empty_10cm/Beaker_Empty_10cm.blend`。项目根目录启动器已改为打开新版本；本目录仅保留过程记录。

在项目根目录双击 `OpenBeakerInBlender.cmd`，或使用 Blender 打开本目录的 `Beaker_Repaired_10cm.blend`。

## 文件

- `Beaker_Repaired_10cm.blend`：可继续编辑的主文件，玻璃材质和未应用的平滑、细分修改器均保留。
- `Beaker_Repaired_10cm.fbx`：已导出平滑后的几何，用于 Unity 等软件。
- `Beaker_Repaired_10cm.glb`：包含玻璃透射材质的交换文件，显示效果取决于导入器是否支持透射材质。
- `after_clay.png`、`after_glass.png`、`after_top.png`：实体、玻璃和俯视检查图。
- `repair_report.json`：源模型哈希、裁切位置、网格统计、实际尺寸和封闭性检查。

## 已执行的修复

基于真实混元输出 `../Hunyuan3D/Outputs/20261022_152840_69d986/model.obj`，不是替换成新建基本体。原始 OBJ、GLB、图片和推理记录保留。

1. 导入并应用轴向转换，使烧杯在 Blender 中沿 Z 轴竖直。
2. 在导入坐标 Z=-0.205 处切除底部大方板，并封闭杯底切口、重算法线。这里封闭的是杯底，杯口仍保留原有内腔。
3. 清除与主体不相连的碎片，使用 Smooth（系数 0.5、25 次）和一级 Subdivision Surface，面使用平滑着色。
4. 按用户确认将最终修改器计算后的总高设为 **0.1 m（10 cm）**，宽度保持修复后的比例，底面中心对齐原点。宽度包含混元生成的杯嘴；没有按 250 mL 容量重新设计。
5. 使用 Principled BSDF：Transmission Weight=1、IOR=1.47、Roughness=0.08。透明效果主要来自透射和折射，节点 Alpha 保持 1。

`01_Original_Hunyuan_Reference` 集合保存原始几何副本，默认隐藏。`02_Render_Staging_NOT_FOR_EXPORT` 是检查用灯光、相机和棋盘地面，默认在视口隐藏，不包含在 FBX/GLB 导出中。正常编辑只选 `Beaker_Repaired_10cm`。

## 在 Blender 里继续调整

- 修改器面板可调整平滑次数、系数或细分等级。保留修改器后再导出时，勾选应用修改器。
- 材质预览可以检查基本材质；使用小键盘 0 进入相机视角、F12 渲染，可以查看这里实际验证过的 Cycles 玻璃效果。
- 如需进一步手工裁切：另存副本，Tab 进入编辑模式，使用 Mesh → Bisect，并按所需方向选择 Clear Inner / Clear Outer；杯底裁切可 Fill，杯口不要直接填平。
- 当前保留混元生成的杯口、杯嘴和内腔形状。内腔较浅、底部较厚，平滑不能把它自动变成精确的工业烧杯。若作业要求真实壁厚或容量，需要再单独重做内腔。

## 放入 Unity 时

优先导入 FBX，检查世界高度应为 0.1 Unity 单位（项目采用 1 单位=1 m）。Blender 的透射节点不会通过 FBX 自动变为 URP 玻璃。

给模型新建 `Universal Render Pipeline/Lit` 材质，Surface Type 设为 Transparent，Base Color 的 Alpha 可从 0.15～0.25 调起，Metallic=0，Smoothness 约 0.9；按视角检查双面显示和透明排序。这是实时透明近似，与 Cycles 折射不同。项目已有 `Assets/_Project/Art/Materials/PCDemo/ReactionVessel_Glass/ReactionVessel_Glass_MAT.mat` 可作为对照。

本次没有替换 Unity 场景里的实验对象；后续替换时只换外观子物体，保留 LabItem、Collider、PourSpout 和液体/泡沫引用。Unity 中的最终视觉效果仍需实际导入检查。

## 复现

在项目根目录运行（输出到本目录）：

```powershell
& 'D:\Blender 4.2\blender.exe' -b --factory-startup -t 8 --python SourceAssets\Models\Hunyuan3D\repair_beaker.py -- SourceAssets\Models\Hunyuan3D\Outputs\20261022_152840_69d986\model.obj SourceAssets\Models\Beaker_Repaired_10cm
```
