# PC Demo 操作与开发

> 此文档记录首版固定顺序 Demo。当前自由配制版本以 [FreeMixDemo.md](FreeMixDemo.md) 为准，程序位于 `Builds/PC/FreeMix`；下文的固定顺序与旧验证说明仅用于历史对照。

## 打开

工程名称已统一为 `MRLab_E06_24371102`，路径为 `D:/UnityHub/Projects/MRLab_E06_24371102`。

Unity 2022.3.62f3c1 打开 `Assets/_Project/Scenes/PC/01_PC_Main.unity`，点击 Play。
当前独立程序：`Builds/PC/FreeMix/MRLab_E06_24371102.exe`。

## 操作

点击试剂瓶，再点击中央容器。顺序：过氧化氢溶液 → 洗洁精 → 食用色素 → 酵母催化液。等待反应完成。

- 数字键 1—4：选择对应试剂。
- Enter：添加选中试剂。
- R 或“重置实验”按钮：随时重置。
- Esc：取消选择。

## 场景和资源

已将旧主场景保存为 `Scenes/Test/91_Test_BeforePCDemo.unity`，其中保留原来的中文测试 UI。正式主场景只停用旧 Canvas 和 EventSystem，新增对象位于标准根节点下的 `PC_Demo` 子节点。

器材 Prefab 位于 `Assets/_Project/Prefabs/Devices` 和 `Interactables`。试剂根节点保留 LabItem、Collider、PourSpout、SelectionRing；替换 Body/Shoulder/Neck 等外观时，保留上述交互节点。Prefab 根节点 Scale 保持 1，模型在子节点对齐尺寸。容器保留 Liquid 和 PourTarget 引用。

菜单 `MRLab > Build PC Demo Scene` 用于重新生成初始 Demo，会重建名为 PC_Demo 的生成内容。**手工调整场景后不要再次运行该菜单**，否则生成内容内的手工布置会被替换；正常编辑和构建使用 Unity 自带菜单即可。

## 检查

`MRLab > Verify Experiment Rules` 检查顺序约束、重复操作、重置和完成状态。独立程序追加 `--demo-verify` 可运行自动冒烟测试（正常运行不启用），输出日志标记 `PC_DEMO_PLAYER_VERIFICATION_PASSED` 和 `Verification/Ready.png`、`Completed.png`。

手动验收：故意选错一次；正常完成四步；分别在倾倒中、反应中及完成后重置；再次完整完成；检查中文文字和鼠标点击。

## 第一版范围

已实现固定视角、点击选择与添加、倾倒动画、液体变色、泡沫示意、步骤提示、纠错计数、反应倒计时、完成提示、重置。
暂未实现自由走动、拖拽抓取、连续手动倾倒、剂量/浓度实验、物理流体、音效、MR/CAVE 输入。这些不是当前初步可操作 Demo 的依赖。

## 缺少模型从哪里找

第一版已自带可替换的器材占位外观，无需购买即可运行。以下是候选来源，尚未下载或集成：

| 用途 | 来源 | 选择建议 |
| --- | --- | --- |
| 桌椅、柜子 | [Kenney Furniture Kit](https://kenney.nl/assets/furniture-kit) | 官方页面标明 CC0，低多边形风格适合后续 Quest |
| 材质、环境物件 | [Poly Haven](https://polyhaven.com/) | [官方许可](https://polyhaven.com/license)为 CC0；优先选择 1K/2K 贴图 |
| 烧杯、锥形瓶、量筒 | [Sketchfab Chemistry Glassware](https://sketchfab.com/3d-models/chemistry-glassware-b8594f7dc7e8442dbaaae7a11da4a962) | 搜索结果显示可下载；实际格式、作者许可、归属要求仍需在下载页面核实 |

搜索词：`beaker low poly`、`Erlenmeyer flask`、`laboratory glassware`、`reagent bottle`、`lab bench`。优先 FBX/OBJ，避免依赖 Blender 程序节点材质；透明玻璃在 Unity 中重新配置 URP 材质。

外部资源先放入 `Assets/ThirdParty/来源名` 并登记 `Docs/ThirdPartyAssets.md`。整理成项目正式资源后使用 `Assets/_Project/Art/Models`、`Art/Materials`、`Art/Textures` 对应目录。Blender 制作源文件放在 `SourceAssets/Models`。注意之前对话中的 `_Project/Models` 是简略建议，本项目应遵循带 `Art` 的课程目录。
