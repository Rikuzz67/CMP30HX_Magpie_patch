<div align="right">

&#x20; <strong>语言 / Language:</strong>

&#x20; <a href="./README.md">English</a> | <a href="./README.zh-TW.md">繁體中文</a> | 简体中文

</div>



\# CMP 30HX · Magpie 社区版 DLSSNR 一键兼容补丁



本项目为 \*\*NVIDIA CMP 30HX\*\*（TU116 架构，无物理 Tensor Core）提供自动化兼容补丁与专用加载器，使其能够在 \[SAOG0721/Magpie](https://github.com/SAOG0721/Magpie) 社区版中成功初始化并运行 DLSSNR（Feature 18）图像滤镜\[cite: 11]。



> \*\*重要说明\*\*：本项目仅针对该软件包中的 \*\*DLSSNR / Feature 18 图像滤镜路径\*\*\[cite: 11]。现有验证不代表在 CMP 30HX 上实现了完整 DLSS5 功能集（如独立超分、光线重建或所有插帧能力）\[cite: 11]。



\---



\## 一、 技术实现原理



CMP 30HX 在硬件层面缺少物理张量核心（Tensor Cores），且无官方显示输出\[cite: 11]。默认状态下调用 DLSS 相关组件会遭遇多层软硬件校验阻断\[cite: 11]：



1\. \*\*硬件白名单拦截绕过\*\*：

&#x20;  \* 原始 `\_nvngx.dll` 在初始化阶段会检查 GPU 架构，检测到不支持的硬件时直接返回 `0xbad00001`（`unsupported hw detected`）\[cite: 11]。

&#x20;  \* 本方案通过定位并修补核心二进制中的 3 处条件跳转分支（将 `0F 85` 修改为强制跳转 `E9`，保留原跳转目标），绕过硬件拦截逻辑\[cite: 11]。

&#x20;  \* 此过程不添加物理运算单元，亦未重训模型\[cite: 11]。

2\. \*\*Magpie 签名校验绕过\*\*：

&#x20;  \* 修改后的 `\_nvngx.dll` 因哈希改变，会被 Magpie 原生加载器以 `0x80096010`（`TRUST\_E\_BAD\_DIGEST`）拒绝，并回退到系统驱动再次触发错误\[cite: 11]。

&#x20;  \* 本工具通过在 `Magpie.exe` 中注入新增的 `.hxload` 节，将本地 NGX 加载调用重定向至配套的 `30HXCoreLoader.dll`\[cite: 11]。

3\. \*\*固定路径与 SHA256 锁定\*\*：

&#x20;  \* `30HXCoreLoader.dll` 使用 Windows BCrypt 密码学 API 计算并严格校对本地 `\_nvngx.dll` 的 SHA256 摘要\[cite: 11]。

&#x20;  \* 只有与已知兼容哈希完全匹配时才会调用 `LoadLibraryExW`，既解除了系统签名限制，又杜绝了任意未知 DLL 的注入风险\[cite: 11]。

4\. \*\*管线对接与输出\*\*：

&#x20;  \* 沿用上游 Magpie 已有的 snippet 直接加载逻辑与模块伪装机制，在 D3D12 管线中完成 Feature 18 创建、Evaluate 执行与 GPU Fence 同步呈现\[cite: 11]。



\---



\## 二、 支持版本与验证状态



本工具内置严格的指纹核对，仅支持 `versions.json` 列出的已知版本，\*\*不支持 Blinue 官方原版，亦不盲目匹配未来未知版本\*\*\[cite: 11]。



| Magpie 版本 | 适配状态 | 对应主包下载 | 说明 |

| :--- | :--- | :--- | :--- |

| \*\*v0.6.11\*\* (实验版) | 独立 D3D12 测试通过\[cite: 11] | \[0.6.11 发布页](https://github.com/SAOG0721/Magpie/releases/tag/v0.6.11-experimental) · \[完整 ZIP](https://github.com/SAOG0721/Magpie/releases/download/v0.6.11-experimental/Magpie-Experimental-x64.zip)\[cite: 11] | 新版适配，内置 310.9.16 模型已过独立验证\[cite: 11] |

| \*\*v0.6.10\*\* (稳定版) | 应用内完整实测通过\[cite: 11] | \[0.6.10 发布页](https://github.com/SAOG0721/Magpie/releases/tag/v0.6.10-experimental) · \[完整 ZIP](https://github.com/SAOG0721/Magpie/releases/download/v0.6.10-experimental/Magpie-Experimental-x64.zip)\[cite: 11] | 经验证持续稳定运行，支持三风格平滑切换\[cite: 11] |



\### 已知文件指纹（SHA256）



\* \*\*NVIDIA 驱动核心\*\*（已验证驱动 616.92）\[cite: 11]：

&#x20; \* 原始 `\_nvngx.dll`：`91d3742f0df3f2dd85141fa39ea257f7b9243c3e6755cd7fea894c721e7491aa`\[cite: 11]

&#x20; \* 修补后 `\_nvngx.dll`：`cf36298fb8bdcf46e0e5d2f558f9132f9aff86a95db5ce5ab849fbacb4083702`\[cite: 11]

\* \*\*已识别的社区模型\*\*\[cite: 11]：

&#x20; \* `310.8.2.0`：`6eb209e764f39872625debd6abaf45e2bb6322f6f270f781f70c059ae30b3927`\[cite: 11]

&#x20; \* `310.9.16.0`：`a8a237a967469c455a84654932b513ab656fbaa1e0c44504b1e1bf0890f902b8`\[cite: 11]



\---



\## 三、 使用教程



补丁包内含全自动脚本，\*\*无需配置 Python、Visual Studio 或手动编译\*\*，直接使用原生 PowerShell 5.1 运行\[cite: 11]。



\### 步骤 1：准备软件

1\. 下载对应的社区版压缩包（如 `v0.6.11-experimental`）并解压到一个新目录（例如 `D:\\Magpie-30HX\\`）\[cite: 11]。

2\. 确保已完全退出正在运行的 Magpie\[cite: 11]。



\### 步骤 2：一键安装补丁

1\. 解压本补丁包，双击运行 \*\*`Install-30HX.cmd`\*\*\[cite: 11]。

2\. 在弹出的文件选择器中，定位并选中新解压目录下的 `Magpie.exe`\[cite: 11]。

3\. 安装脚本将自动执行以下操作\[cite: 11]：

&#x20;  \* 自动在系统 DriverStore 中只读检索受支持的 `\_nvngx.dll` 核心（若未找到，脚本会提示你手动指定备份核心，绝不修改系统驱动）\[cite: 11]。

&#x20;  \* 在应用本地生成专用的兼容核心副本\[cite: 11]。

&#x20;  \* 注入 `.hxload` 节区并部署 `30HXCoreLoader.dll`\[cite: 11]。

&#x20;  \* 核对社区 DLSSNR 模型指纹\[cite: 11]。

4\. 终端提示 \*\*`INSTALL\_OK`\*\* 即表示安装成功\[cite: 11]。



\### 步骤 3：配置推荐参数

1\. 启动打好补丁的 `Magpie.exe`\[cite: 11]。

2\. 在 \*\*效果组\*\* 中新建或编辑配置项\[cite: 11]：

&#x20;  \* 添加滤镜：`DLSSNR\_AI\_Filter`\[cite: 11]

&#x20;  \* \*\*输入缩放\*\*：将宽高各设为 \*\*25%\*\*（像素量为原图 6.25%，平衡 TU116 算力开销）\[cite: 11]

&#x20;  \* \*\*光流设置\*\*：初始测试请先\*\*关闭光流\*\*\[cite: 11]

&#x20;  \* \*\*帧率目标\*\*：在“帧率与刷新”中将内容帧率从较低目标（如 10–15 FPS）起步测试\[cite: 11]

3\. 激活缩放后，在日志中确认初始化状态，并尝试切换三种滤镜风格\[cite: 11]。



\---



\## 四、 维护与检查工具



\* \*\*完整性检查 (`Check-30HX.cmd`)\*\*：双击运行并选择目标 `Magpie.exe`，脚本会比对主程序、本地核心、加载器与模型的 SHA256，核对备份是否完整\[cite: 11]。

\* \*\*一键还原 (`Restore-30HX.cmd`)\*\*：退出 Magpie 后运行此脚本，将自动删除新增的 `30HXCoreLoader.dll` 和本地核心，并从原始备份目录（`.30hx-compat-backup-v1`）完整还原未修补的 `Magpie.exe`\[cite: 11]。



\---



\## 五、 源码与本地编译



仓库提供完整的注入引擎与加载器源码，供审计与二次构建\[cite: 11]：



```text

Magpie-30HX-Compat/

├── src/

│   ├── PatchEngine.cs         # C# 编写的 PE 注入、节区扩充与哈希修补引擎

│   └── pinned\_loader.cpp      # 基于 Windows BCrypt 实现的固定哈希核心加载代理

├── Install.ps1                # 自动化安装与检索逻辑

├── versions.json              # 支持的 EXE、核心与模型指纹清单

├── CHECKSUMS.json             # 补丁包自身的哈希清单

├── Install-30HX.cmd           # 一键安装入口

├── Check-30HX.cmd             # 一键校验入口

└── Restore-30HX.cmd           # 一键卸载与还原入口

