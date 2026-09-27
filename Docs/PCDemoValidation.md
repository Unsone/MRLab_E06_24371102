# PC Demo 验证记录

## 自由配制版本（2026-09-27）

- 最新程序：`Builds/PC/FreeMix/MRLab_E06_24371102.exe`（项目更名后重新构建）。
- `Logs/FreeMixBuild.log`：规则测试通过；24 种排列全部接受；验证色素可选且不改变产气量、无洗洁精仅产气、后加洗洁精仅捕集剩余气体、耗尽后不重启、重置取消未提交添加。
- 最终构建：0 errors，2 warnings。两项均为已有 Meta XR 项目设置和 Direct3D11 提示。
- `Logs/FreeMixPlayer.log`：`PC_DEMO_PLAYER_VERIFICATION_PASSED`。验证真实倾倒回调、逆序添加、白泡沫、下落堆积、无洗洁精气泡、耗尽后补加、染色及中途重置。未发现运行时异常。
- 连续泡沫运行检查：189 个运动控制点，记录到最高点约为瓶底上方 0.61 米，最终控制点全部落到托盘堆积区域。这些值仅用于视觉测试，不是化学测量结果。
- 已查看最终实际渲染截图：`Builds/PC/FreeMix/Verification/01_WhiteBurst.png`（无色素喷出）、`03_WhitePile.png`（落下堆积）。截图包含完整中文 UI，不是后台黑图。
- 最终特效为连续光滑管状网格，使用重力、长度约束和自接触表现软条堆积；非 CFD。白色高光已降低并加入程序生成的细微表面法线。
- `Builds/PC/Demo` 曾被首次构建写入，不作为旧版只读快照；以 FreeMix 为当前交付。未提交或推送 Git。

## 首版记录（历史）

日期：2026-09-26。Unity 2022.3.62f3c1，Windows x86-64，URP。

- 编辑器规则测试：`PC_DEMO_RULE_TESTS_PASSED`。覆盖乱序、重复添加、各添加阶段重置、无效计时、完成锁定和重做。
- Windows 构建：`PC_DEMO_BUILD_PASSED: 0 errors, 2 warnings`。日志位于 `Logs/PCDemoBuild.log`。
- 独立程序使用真实 D3D11 / AMD Radeon 780M 运行。日志 `Logs/PCDemoPlayer.log` 包含 `PC_DEMO_PLAYER_VERIFICATION_PASSED`，未发现运行时异常或缺字警告。
- 独立程序测试覆盖：错误顺序保持状态；倾倒 0.3 秒后重置且等待后不再推进；四步依次完成；反应中重置并清空粒子；重新完成四步和反应；完成后重置恢复初始状态。
- 实际窗口检查：初始界面、中文步骤、三维器材、错误计数、试剂倾倒画面已显示正常。正在使用的运行窗口保持打开供用户体验。
- 后台隐藏窗口运行生成的 `Verification/*.png` 为黑图，不能作为视觉验收或作业截图；截图请从正常可见的运行窗口获取。

两项构建警告来自已有 Meta XR 检查：未配置 Oculus/OpenXR provider，以及手动选择 Direct3D11。未影响本次 PC 构建；进入 MR 阶段再按设备要求处理。

第一版仍使用占位模型与示意泡沫，暂不具备定量模拟、自由拖拽和音效。原中文字体和场景的未提交修改已保留；修改前主场景另存为 `Scenes/Test/91_Test_BeforePCDemo.unity`。本次未执行 Git 提交或推送。
