# 资源清单

| 资产基础名称 | 类型 | 用途 | 正式资源路径 | 来源文件路径 | 状态 |
| --- | --- | --- | --- | --- | --- |
| LabBench | Primitive Prefab / Material | 实验台 | `Assets/_Project/Prefabs/Environment/LabBench.prefab`、`Art/Materials/PCDemo/LabBench` | `Scripts/Utility/Editor/PCDemoBuilder.cs` | 初步几何外观 |
| ReactionVessel | Mesh / Material / Prefab | 反应容器 | `Art/Models/PCDemo/ReactionVessel`、`Art/Materials/PCDemo/ReactionVessel_Glass`、`Prefabs/Devices/ReactionVessel.prefab` | 同上 | 程序生成空心网格 |
| Reagent_Peroxide / Soap / Color / Catalyst | Primitive Prefab / Material | 试剂选择和倾倒 | `Prefabs/Interactables/Reagent_*.prefab` | 同上 | 初步几何外观，根节点 Scale=1 |
| Tray | Primitive Prefab / Material | 托盘 | `Prefabs/Devices/Tray.prefab`、`Art/Materials/PCDemo/Tray` | 同上 | 初步几何外观 |
| Foam | Runtime Mesh / Prefab / Material | 连续泡沫喷出、下落和自接触堆积 | `Prefabs/Effects/Foam.prefab`、`Art/Materials/PCDemo/Foam`、`Art/Materials/PCDemo/Foam_Dyed` | `Scripts/Experiment/ViscousFoam.cs` | 连续管状网格，区分白色与染色 |
| ReactionBubbles | Particle Prefab | 气泡 | `Prefabs/Effects/ReactionBubbles.prefab` | 同上 | 最多 60 粒子 |
| Foam_Normal | Generated Texture | 白色与染色泡沫的细微表面起伏 | `Art/Textures/PCDemo/Foam/Foam_Normal.png` | `Scripts/Utility/Editor/PCDemoBuilder.cs` | 程序生成，128 × 128，Normal Map |
| LabHUD | TMP UI | 教学提示与重置 | `Scenes/PC/01_PC_Main.unity` 中 `_UI/PC_Demo` | 同上 | 随场景生成 |

表中 `Art`、`Prefabs`、`Scripts`、`Scenes` 路径均相对于 `Assets/_Project`。本阶段无外部模型或纹理依赖；几何占位使用共享 URP 材质，正式美术替换时进一步统一模型与多材质部件的命名。

同一资产的模型、材质和贴图使用统一基础名称，并在 Models、Materials、Textures 下保持一致的相对子目录结构。
