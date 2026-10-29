# Hunyuan3D-2.1 本地 AMD 建模

本项目使用官方 Hunyuan3D-2.1 的预训练形状模型，通过 Windows DirectML 尝试在 Radeon 780M 上推理。适配层位于 `amd_backend.py`，官方形状代码及许可证保留在 `vendor`。没有安装 CUDA 纹理扩展，也没有更换显卡驱动。

2026-10-22：已补上本地前景提取与采样盒退化检测。烧杯完整回归已生成杯身、杯口和内腔；多余底板和误建的水面隔板在 Blender 阶段去除，并补齐下部内壁，另存总高 10 cm、内深约 9.55 cm 的空烧杯，见 [空烧杯修复说明](../Beaker_Empty_10cm/README.md) 与 [建模清单与对比](../../../Docs/HW02_建模清单与对比.md)。旧的“生成完成”仅表示程序执行结束，不能作为几何正确性的证明。

## 使用

1. 在工程根目录运行 `GenerateModel.cmd`。首次安装使用独立 Python 环境，下载约 7.37 GB 形状权重并校验 SHA-256。中断后重复运行可以续传。
2. 浏览器打开 `http://127.0.0.1:7861`，选择单个物体的参考图片，选择 AMD DirectML、步数、分辨率和种子。
3. 首次连通性检查可点击“使用官方测试图”。本机已验证 4 步、64 分辨率能导出网格，但结果只有粗糙杆件；2 步测试没有有效表面。该图不属于课程实验器材；低步数结果不能作为正式建模质量证明。
4. 正式器材建议先尝试 20 步、128 分辨率；此为待按实际耗时和质量调整的起点。优先使用透明背景 PNG；无有效透明遮罩时自动通过本地 U2Netp/CPU 提取前景。网页显示实际模型输入预览，遮罩几乎全空或全满时会拒绝继续。
5. 成功后查看网页中的真实 OBJ 线框预览，下载 OBJ/GLB。每次运行保存在独立的 `Outputs/<时间与编号>` 目录中。

建议生成期间关闭 Unity 和 Blender，给共享内存留出空间。网页可以取消当前任务，保留输入和日志。DirectML 部分算子可能回退 CPU，因此不能称为全 GPU 推理。CPU 选项会重新运行生成，不会自动悄悄切换设备。

## Unity / PC 集成

- 编辑器菜单：`MRLab > AI Modeling > Start Hunyuan3D`。
- 正式 PC 主场景启动后，右上角显示“AI 建模 / F8”。可以打开项目内的本地生成工具，并在生成完成后点击“载入本次生成结果预览”。
- 运行时从本地服务读取真实 OBJ，进行坐标转换、网格索引检查及法线计算，在独立的预览相机中显示。滑条控制旋转。此步骤没有替换实验器材。
- PC 构建需要放在完整工程的 `Builds/PC` 下，才能找到配套的 Python 工具和环境；仅复制 EXE 不能运行算法。
- 本地服务默认地址固定为 `127.0.0.1:7861`，不向云端发送参考图。端口正在使用时复用已有同址服务；应只运行本项目的一个建模服务。
- 原始模型进入 Blender 修补、尺寸校正、UV/材质处理后，再导入 `Assets/_Project/Art/Models`。替换实验器材时保留原有 LabItem、Collider、PourSpout 及液体/泡沫引用。

## 运行证据

每次运行保存 `reference.png`、原始上传字节、`input.png`、`console.log`、`run.json`。成功时额外保存 `model.obj`、`model.glb`、扩散潜变量 `latents.pt`。运行记录包含真实后端、固定权重版本及哈希、输入和输出哈希、参数、耗时、模型顶点和面数。失败只记录失败原因，不导出替代模型。

前景处理额外保存 `prepared.png`、`foreground_mask.png`、`prepared_preview.png` 和 `preprocessing.json`。若已经导出的网格被识别为采样盒退化，保留原始 OBJ/GLB 供诊断，但任务会标记失败，并保存 `mesh_quality.json`。

`run.json` 中的进程内存读数是阶段采样，不能当作专用显存峰值。780M 使用共享内存。低内存保护会在可用内存持续不足时停止任务。

官方源码固定提交：`82920d643c0dc2f7bfd7255f45f62d386edfe60c`。
官方权重固定提交：`0b94677654c57bb9a6b6845cd7b704ccf551d327`。
权重 SHA-256：`6b519fc7242f78e9b5f47ea4d55668fe3d944a2d27332f4ca68d29a6ff603f5e`。

## 适配方式与边界

- 使用官方网络、权重和 Flow Matching 调度器。
- meta 初始化和内存映射加载避免先构造一份完整随机权重；图像编码器、DiT 和 VAE 分阶段加载。
- DirectML 的 DiT 使用逐块 CPU offload：每个 Transformer 块进入计算前搬到显卡，计算后恢复原始内存映射权重。这样降低 GPU 驻留权重大小，但增加传输和运行时间。
- 采样和 VAE/体素解码在两个独立进程中顺序执行，避免上一阶段 DirectML 缓存挤占解码内存。体素块逐个回读 CPU，限制异步任务积累。
- 若采样已保存 `latents.pt`，可使用 `generate.py --input <该次输入图> --decode-from <该次输出目录> --resolution 64` 从真实潜变量重新解码；会核对模型和输入记录，并创建新的结果目录，不覆盖旧证据。
- 将注意力沿 query 分块，降低中间矩阵内存；CFG 的条件/无条件分支顺序计算，降低批次内存。
- AMD 使用 FP16；CPU 路径使用 FP32。数值变化和低精度兼容性需要结合真实推理检查。
- 仅适配形状模型；不包含 PBR 贴图生成、Blender 优化；当前器材清单为两种几何模型、五个器皿实例。

快速数值检查：在此目录运行 `.runtime\venv\Scripts\python.exe test_adapter.py`。该检查使用小网络和数值对照，不能代替完整预训练推理。Unity 的 `unity-verification.json` 仅说明实际编译、OBJ 转换及预览 Shader 检查通过；实际显示情况另看运行截图和记录。

来源：[腾讯官方代码](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1)、[官方权重](https://huggingface.co/tencent/Hunyuan3D-2.1)、[微软 DirectML 文档](https://learn.microsoft.com/en-us/windows/ai/directml/pytorch-windows)。
