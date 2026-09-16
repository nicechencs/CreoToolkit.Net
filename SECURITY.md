# Security Policy

## Supported Versions

CreoToolkit 仍在 pre-1.0 阶段，安全修复仅针对 `main` 分支最近的发布。

## 报告漏洞

**请不要**在公开 Issue 里报告安全漏洞。

请通过 **GitHub Private Vulnerability Reporting** 私下联系维护者：

- 仓库 → **Security** 页 → **Report a vulnerability**
- 详细步骤见 [GitHub 官方文档](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)

我们会在 **3 个工作日**内回复确认收到，**14 天**内给出修复时间表。

## 范围内

CreoToolkit 是连接 .NET 与 PTC Creo Toolkit 的 SDK，主要安全敏感面包括：

- P/Invoke 边界处的缓冲区越界 / 类型不匹配
- `SafeHandle` 与三态值的释放语义
- 反序列化（绑定 IR JSON、配置文件）
- 外部输入（Creo 模型文件路径、运行时配置）

## 范围外

- **PTC Creo 本身的安全问题** — 请直接联系 PTC（https://www.ptc.com/）。
- **用户业务层代码的安全问题** — 由该业务的维护者负责。
- **依赖项（.NET runtime / NuGet 包 / libclang）** — 请按其各自项目流程报告。

## 已知限制

- 当前版本不针对恶意 Creo 模型文件做硬化（attack surface 极小，但未明确扫描过）。
- L1 native 层使用 C++，可能存在常见 UB 风险；以 `ASAN` / `UBSAN` 验证过的部分会在 CHANGELOG 标注。
