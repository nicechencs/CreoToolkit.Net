# caller-buffer-candidates.md
> 本目录 interop-review-notes.md §6 结论已应用：从 `ownership.yml` 的 `mode: data` 项中剔除 A 类 caller-buffer 误登记。
> 判据：
> - **真堆 producer（保留在 functions 表）**：`Type**` 双指针出参（含 `Type***`），或 `ProArray*` 单层指针（由 ProArrayAlloc 分配），均存在配对 `*Free`（ProArrayFree / ProWstring*Free / 各 *arrayFree）。
> - **A 类 caller-buffer（移出 functions 表，列于此备查）**：定长数组 typedef 单缓冲 / 单层 `char*`/`wchar_t*` 直接写入调用方提供的缓冲，无配对 `*Free`（§6.5）。

## 结论（2026-06-14）

对 `ownership.yml` 中全部 **187 个 `mode: data` 出参**（189 个登记函数，含 4 个 confirmed:true）逐一从头文件 `<creo-sdk>/protoolkit\includes` 提取真实签名核实指针深度：

| 出参形态 | 数量 | 判定 | 处理 |
|---|---|---|---|
| `Type**`（双指针，ProArray / ProWstring 等，配对 *Free） | 134 | 真堆 producer | 保留 |
| `Type***`（数组的数组，wchar_t*** 等，配对 *Free） | 29 | 真堆 producer | 保留 |
| `ProArray*`（单层，ProArrayAlloc 分配，配 ProArrayFree） | 23 | 真堆 producer | 保留 |
| `ProName/ProPath/ProLine` 等定长 typedef **单缓冲**（caller-buffer，无 *Free） | **0** | A 类 caller-buffer | 移出（无） |

说明：

- 元素为定长 typedef（`ProName`/`ProPath`/`ProLine`/`ProMdlName`/`ProMdlFileName`/`ProFileName`，均 `typedef wchar_t X[SIZE]`，见 ProSizeConst.h）的项，其**出参签名仍是 `ProName**` / `ProPath**`（即装入函数分配的 ProArray）**，配 ProArrayFree。这是真堆 producer，**不是** caller-buffer。
- 例：`ProConfigoptArrayGet` 出参 `ProPath** value_array`，头文件 ProUtil.h:294 明示 "The function allocates this array, free it with ProArrayFree()" → 保留。
- `wchar_t**` 系（ProServer*/ProUI*/ProGtol* 等）出参均为函数分配的堆 ProWstring/ProArray，配 ProWstringFree/ProWstringarrayFree/ProArrayFree → 保留。

**最终：A 类 caller-buffer 误登记候选 = 0。无需从 functions 表移出任何项。**

这与 §6 结论"低风险、过度登记只是多几条 confirmed:false"的判断一致：初稿生成阶段实际已过滤掉单层定长 caller-buffer 出参，留在 data 表的全部是 `Type**`/`Type***`/`ProArray*` 双指针堆 producer。各项仍保持 `confirmed:false`，由真机运行 + 泄漏测试兜底验证嵌套 free 完整性（F7/F8）。
