# ownership.yml 判定说明

> 早期生成器从 `ir.json` + 头文件注释产出初稿后，下列 6 条存疑项改为手工判定。
> **所有 `confirmed:false` 项以泄漏 / UAF 测试在真机验证阶段兜底。**
> 现状:自动推导脚本已停用,`ownership.yml` 为**手工维护的权威来源、禁再生**;后续修订直接改 yml,不再自动产出。

## 结论

1. **ProElementArrayGet / ProElementChildrenGet —— 高危，纠偏（最重要）**
   - 问题：子节点 getter 头文件无 free 说明，返回的 `ProElement` 很可能是**借用父 elemtree 内部指针**，非 owned。
   - 风险：启发式标 `live+owned` → 对父树内部指针调 `ProElementFree` → 崩溃（F12 类）。
   - **结论**：这类「父对象内部子项 getter」**默认不 owned**（视为借用，不 free）。ownership.yml 中标 `mode: live, owned: false`（或单列待定），**确凿前绝不自动 free**。**运行时验证最高优先**核实 Element 子项的所有权。
   - 推广原则：**「容器内部子项 getter」默认借用不 owned；只有「分配新对象的 producer」才 owned。** 这是对 §5.2 启发式的补丁（启发式只看返回类型，漏了「子项 vs 新分配」之分）。

2. **ProElementReferencesGet 的 element_free vs array_free**
   - 结论：**优先 `array_free`**（Creo 主流惯例：`ProReferencearrayFree`/`ProSelectionarrayFree` 注释自带「also frees each member」）。§5.2 示例里的 `element_free: ProReferenceFree` 仅为说明逐元素语义。
   - L1 的 `CreoToolkit_Free` kind 分派据此：数组级 free 存在时调数组级（它自会递归元素），不要再逐元素 + 数组双重释放（防 double-free）。manifest 同时记录数组级 free 与元素类型，供 L1 决策。

3. **IR 真实参数名 vs 方案示意名（`p_elem` vs `p_elemtree`、`model_file_types` vs `p_model_file_types`）**
   - 结论：**一律以 IR 真实名（头文件解析所得）为准**——这正是「头文件唯一权威来源」原则本身。设计文档 §5.2 的命名是示意，不构成约束。用 IR 名 + 加注差异，做法正确。

4. **ProSelectionarrayToReferences / ProReferencearrayToSelections（转换函数，live）**
   - 结论：接受 `live` + `array_free`（输出为新分配 owned 数组，头文件明示需对应 array-free）。输入与输出各自 owned、分别收敛，不交叉 free。元素是否共享底层 select3d 不影响：各自 array-free 释放自己那层。保持 confirmed:false，泄漏测试兜底。

5. **ProSelbufferSelectionsGet（live，疑似 session 缓冲）**
   - 结论：暂按 `live`+array_free（头文件要 free），但**标记需排除「类 ProSelect 的 static/reallocated 缓冲」**。真机验证核实:若「下次调用即重分配」→ 重分类为 `borrowed`（即拷不放）。中高优先。

6. **185 个 data 项混入 caller-buffer 串出参（过度登记）**
   - 结论：低风险（过度登记只是多几条 confirmed:false，不会不安全）。下一轮精化：`Type**` 且存在配对 `*Free` → 真堆 producer（data/live）；`char[]`/定长 `ProName/ProPath` 风格 → A 类 caller-buffer（§6.5），从 producer 表剔除。

## 状态
- ownership.yml 仍为**初稿**：4 项 confirmed，185 项待真机验证 + 人工复核。
- 上述结论 1（Element 子项不 owned）已应用：ownership.yml 中 `ProElementChildrenGet`/`ProElementArrayGet` 标 `owned:false`+`borrowed_or_unknown:true`+CAUTION 注释，L1 绝不自动 free，真机验证兜底。
