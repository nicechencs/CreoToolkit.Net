# Contributing to CreoToolkit

感谢愿意为 CreoToolkit 贡献。本文档说明如何报 bug、提 feature、贡献代码。

## 报 Bug

在 [Issues](../../issues) 用 **Bug Report** 模板。请尽量提供：

- Creo 版本（如 `Creo 4.0 M140` / `Creo 9.0.6.0`）
- 进程内运行时：.NET Framework 4.7.2/4.8（不是 .NET 8 Desktop Runtime）
- 本机构建用的 `dotnet --version`（SDK，仅编译）
- 最小可复现代码片段
- 实际行为 vs 期望行为
- 完整堆栈或日志（开启 `CTK_LOG=on CTK_LOG_LEVEL=trace`）

## 提 Feature

先开 Issue 讨论再写代码。CreoToolkit 是底层 SDK，API 表面改动需要兼顾跨版本稳定性，欢迎在 Issue 里先讨论设计。

## 贡献代码

1. Fork 仓库，从 `main` 切分支：`feat/<topic>` / `fix/<topic>` / `docs/<topic>`。
2. 本地构建：
   ```pwsh
   dotnet build CreoToolkitRefactor.slnx
   ```
3. 如果改了 L1 native，构建 native host：
   ```pwsh
   .\build_native.cmd CreoToolkit.NativeHost Release
   ```
4. Commit 信息中文 / 英文皆可，前缀建议：`feat` / `fix` / `refactor` / `docs` / `test` / `build` / `chore`。
5. 提 PR，CI 通过后等 review。

## 编码约定

- 全仓 **UTF-8 无 BOM**。
- C# 跟随 `.editorconfig`（如有），4 空格缩进。
- C/C++ 编译加 `/utf-8`，中文注释 OK。
- `.cmd` 批处理注释保持 **ASCII**（cmd.exe 936 代码页对 UTF-8 注释不友好）。
- 注释默认极简，只解释 *为什么*；不解释 *做了什么*（代码自身说明）。

## License of Contributions

提交 PR 即表示你同意自己的贡献以 **Apache-2.0** 许可发布，并保证你拥有提交该贡献的合法权利。
