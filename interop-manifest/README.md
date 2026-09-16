# interop-manifest · 内存所有权编目

本目录是 CreoToolkit 与 Creo Pro/Toolkit C API 之间 **P/Invoke 内存所有权契约**的
编目。跟运行时代码一起演进，配合 `ownership.yml`（同目录，2500+ 行）作为 L1
native 层的所有权真源。

## 三态所有权模型

Pro/Toolkit 每个出参的内存归属只可能落到三种状态之一，L1 释放门（`CreoToolkit_Free` /
`Ctk_FreeBuffer`）与生成器都靠这个分类做决策：

| 态 | 语义 | 示例 |
|---|---|---|
| **data** | copy-out-free-in：函数分配、调用方释放；L1 拷成 managed 内存后立即调对应 `*Free` | 大量 `Type**` / `ProArray*` 出参 |
| **live** | 留 owned 句柄，L3 对象持有；显式 Dispose 或 finalizer 主线程释放 | ProSelection 数组、Reference 数组、ProFeatureElemtreeExtract |
| **borrowed** | 库内部 static / 父对象子项 / 下次调用即重分配；**任何一层都不许 free** | ProSelect 的 static 缓冲、ProElementChildrenGet 的父树内部指针 |

错标一态就是崩溃或泄漏。这个目录记录了每一次判定与依据。

## 4 篇文件各是什么

| 文件 | 用途 |
|---|---|
| [`caller-buffer-candidates.md`](./caller-buffer-candidates.md) | A 类 caller-buffer 误登记候选审计（定长 typedef 单缓冲，无 `*Free`）。复核结论：Creo4 M140 全 187 个 data 项过滤后 caller-buffer 误登记 = 0。 |
| [`free-catalog.md`](./free-catalog.md) | Pro/Toolkit `*Free` 函数编目（245 个），含嵌套 free（F7/F8 高危）与命名异义/多变体家族分布。free-dispatch 决策的输入。 |
| [`interop-review-notes.md`](./interop-review-notes.md) | `ownership.yml` 中 6 条存疑项的判定说明（Element 子项 owned:false、array_free vs element_free 统一、IR 真实参数名优先等）。 |
| [`unconfirmed.md`](./unconfirmed.md) | 所有 `confirmed:false` 与 `mode:borrowed/live` 项清单——需人工确认或泄漏门兜底验证；按高危区（§0）/live（§A）/borrowed（§B）/data confirmed:false（§C）分组。 |

配套数据文件：

- `ownership.yml`（**手工维护真源**，禁自动再生）——每个函数的出参 mode/free/owned 三段登记。
- `free-catalog.json`——`free-catalog.md` 的机读版本。

## 面向谁

- 想理解或校验 CreoToolkit 与 Creo 之间内存所有权契约的**贡献者**。
- 添加/修改绑定时，需要判断某个新 Pro API 出参该标 data / live / borrowed 的**开发者**。
- 复核泄漏门失败根因（分配器不配对、`*Free` 家族选错、双重释放、UAF）的**维护者**。

若有具体条目的所有权疑问，先查 `ownership.yml`（登记）→ `unconfirmed.md`（复核进
度）→ `interop-review-notes.md`（同类存疑项结论）→ `free-catalog.md`（选择正确的
`*Free`）。
