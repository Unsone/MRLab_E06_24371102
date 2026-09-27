# MRLab_E06_24371102

## 项目信息

- 项目名称：MRLab_E06_24371102
- 实验名称：大象牙膏实验
- 学生姓名：赵开明
- 学号：24371102
- Unity 版本：2022.3.62f3c1
- 渲染管线：Universal Render Pipeline 14.0.12
- 输入系统：Unity Input System 1.14.2
- MR SDK：Meta XR SDK Core 201.0.0
- 当前开发阶段：PC
- 当前版本：0.1.0

## 已完成功能

- 创建 Unity 工程并锁定课程指定版本。
- 配置 Linear 色彩空间和 Unity Input System。
- 建立 PC、MR、CAVE 共用的规范目录结构。
- 建立 PC 正式主场景和基础 Hierarchy。
- 配置 URP 渲染管线。
- 导入 TextMeshPro Essential Resources。
- 配置 Windows x86-64 正式构建场景。
- 新增实验台、四种试剂、空心反应容器与托盘的初步 PC Demo。
- 鼠标点击选择/添加，数字键快捷选择，倾倒动画与任意顺序配制；色素可选。
- 液面染色、按成分产气、连续泡沫喷出/下落/堆积、结果显示和任意阶段重置。
- 实验规则与 PC 输入分离，可复用至后续 MR/CAVE。

## 构建

- PC 主场景：`Assets/_Project/Scenes/PC/01_PC_Main.unity`
- PC 输出目录：`Builds/PC`
- PC 目标平台：Windows x86-64
- 最新 Demo 可执行文件：`Builds/PC/FreeMix/MRLab_E06_24371102.exe`；请以 FreeMix 目录为准。
- 新版规则与限制：[FreeMixDemo.md](FreeMixDemo.md)。
- 操作与模型替换说明：[PCDemoGuide.md](PCDemoGuide.md)。

## 已知限制

- 第一版为固定视角、点击添加的教学模拟；尚无自由走动、拖拽抓取、定量配液和音效。
- 反应动画为定性示意，12 秒为演示时长，不代表真实反应测量值。
- Meta XR SDK 仍保持已安装的 201.0.0，PC 阶段不变更 SDK。
