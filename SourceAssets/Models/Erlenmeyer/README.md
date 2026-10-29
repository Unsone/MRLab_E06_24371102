# 锥形瓶：混元原始模型与 Blender 修复版

## 原始生成
- `Raw/attempt02/model.obj`、`model.glb`：未经修复的 Hunyuan3D 原始结果，保留不覆盖。
- `Raw/attempt02/run.json`：官方权重、AMD Radeon 780M DirectML、30 步、128 分辨率、seed 42，完成耗时约 54 分钟。
- 第一轮为失败的薄片结果，仅在本地保留，不随源码分发。

## 最终文件
- `Final/Erlenmeyer_Empty_13cm.blend`：可编辑 Blender 文件，带玻璃材质；隐藏的剖面对象只用于检查，不属于导出的模型。
- 同名 `.fbx`、`.glb`、`.obj`：只导出完整锥形瓶。OBJ 不承载完整玻璃节点效果，优先使用 Blender 或 GLB。
- 总高暂设为 **13 cm**，这是可调整的工作尺寸，不是从照片推算出的实测尺寸；照片上的 250 mL 标记不代表本模型容量已校准。

## 修复与检查
保留混元生成的外形，删除瓶颈下方误生成的水平液面隔板，按照外壁轮廓补齐锥形内壁到真实瓶底。修平瓶口并平滑表面，设置玻璃折射率 1.47、透射 1、粗糙度 0.08。

`Final/repair_report.json` 记录原始模型哈希、网格检查、5 条向下内腔探测和 168 条径向壁厚探测。`final_section.png` 展示贯通至底部的空腔；`final_clay.png` 用于检查表面，`final_glass.png` 展示玻璃效果。

修复脚本：`repair_flask.py`。修复结果已接入 PC 主场景；这是视觉模型，交互使用独立根节点及简化碰撞体。
