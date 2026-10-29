# 大象牙膏虚拟实验

虚拟现实基础应用技术课程项目 · 赵开明 · 24371102

当前交付为 **Windows PC 版**：混元生成的烧杯与锥形瓶经 Blender 修复后用于实验，支持自由视角和拿取、倾倒、放下烧杯。实验桌保留为场景设施，托盘已移除；VR 和 CAVE 仍是后续开发目标。

## 运行项目

**取得完整作业包时**：双击根目录的 `启动PC实验.cmd`。正式程序为 `Builds/PC/MRLab_E06_24371102.exe`，请保留整个 `Builds/PC` 文件夹。

**从 GitHub 克隆时**：使用 Unity Hub 和 **Unity 2022.3.62f3c1** 打开仓库，等待包与资源导入，打开 `Assets/_Project/Scenes/PC/01_PC_Main.unity`，点击 Play。需要 Windows 程序时，安装 Windows Build Support，并使用 **MRLab → Build Current PC Scene** 构建到 `Builds/PC`。不要使用会重建旧场景的 **Build PC Demo Scene**。

GitHub 保存源码、模型和作业文档，不包含 PC 构建、Unity 缓存及 AI 权重；仅下载源码后不能直接运行启动脚本。

## 实验操作

| 操作 | 功能 |
| --- | --- |
| 左键点击试剂烧杯 | 拿起，烧杯随鼠标移动 |
| 手持时点击锥形瓶 | 添加一份试剂，保留手中的空杯 |
| 点击反应区外的空闲桌面 | 放下烧杯 |
| 右键拖动 / 中键拖动 / 滚轮 | 旋转 / 平移 / 缩放视角 |
| Home / Esc / R | 恢复视角 / 放回拿起位置 / 重置实验 |
| F8 | 打开本地 AI 建模面板 |

每杯试剂只能添加一次，重置后补满。反应为教学演示，目前不支持按倾角连续计量。

## 文档与模型

- [项目说明与操作方法](Docs/README.md)
- Unity 主场景：`Assets/_Project/Scenes/PC/01_PC_Main.unity`
- Unity 版本：`2022.3.62f3c1`

- [建模清单与修复前后对比](Docs/HW02_建模清单与对比.md)：两种几何模型，组成一只锥形瓶和四只试剂烧杯。
- [空烧杯原件与修复说明](SourceAssets/Models/Beaker_Empty_10cm/README.md)、[锥形瓶原件与修复说明](SourceAssets/Models/Erlenmeyer/README.md)：保留原始生成结果与可编辑 Blender 文件。
- [Hunyuan3D 本地工具](SourceAssets/Models/Hunyuan3D/README.md)：运行根目录 `GenerateModel.cmd` 配置环境；首次下载约 7.37 GB 形状权重。已在 AMD Radeon 780M / DirectML 上完成器皿推理，不包含自动贴图。推理耗时较长，普通实验运行无需启动它。
- [PC 验证记录](Docs/PC验证结果.txt)：2026-10-28 无托盘版本通过 31 项自动运行检查；不代表 VR/CAVE 真机验收。

## 第2次小作业材料

`Docs` 保留课程要求的五项材料，编号前缀为 `24371102+赵开明-`：

| 文件 | 内容 |
| --- | --- |
| `21-算法描述.txt` | 算法、版本与生成参数 |
| `22-算法输入.zip` | 参考图片与预处理输入 |
| `23-算法结果.zip` | 未经修复的生成模型与运行记录 |
| `24-软件描述.txt` | Blender 版本与优化方法 |
| `25-软件结果.zip` | 修复后的模型与检查记录 |

完整作业 ZIP 和 PC 构建在本地另行分发，不随源码提交。资源来源和许可证见 [项目说明](Docs/README.md#资源来源)；腾讯模型、第三方字体及 Unity/Meta 包按各自许可使用。
