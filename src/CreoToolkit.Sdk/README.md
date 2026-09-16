# CreoToolkit.Sdk · L3 对象模型速查

> 改 SDK 代码前 30 秒先扫这页 — 六原则 + 五条命名规则 + 类型骨架 + 验收标准 + checklist。

## 0. 一句话

**.NET 对 C/C++ Creo Toolkit 的对象化封装**;业务用 .NET 写;native 该写就写。**地道、不割裂、可证零泄漏** 是验收口径。

## 1. 六大原则(顶层,改 API 必读)

| | 原则 | 怎么落 |
|---|---|---|
| **P1** | 读写分离 | 现单类 + 方法 |
| **P2** | 对象=会话绑定的视图句柄 | session 关后调 native → 抛 `CreoSessionClosedException`;DATA 已 copy-out 仍可读 |
| **P3** | 工厂构造,禁裸 new | 所有 public 对象 ctor `internal`/`protected`;经 `CreoSession.Models.Retrieve/Current/CopyTo` 等工厂 |
| **P4** | 异常层级精确映射错误码 | `CreoException`(动作失败) / `CreoSessionClosedException`(P2);按 `ProError` 细分 |
| **P5** | 部分覆盖 + 显式降级通道 | 主 L3 面**无** `IntPtr/NativeMethods`;降级走独立 `CreoToolkit.Sdk.Advanced`(DATA token,按需开口子) |
| **P6** | 类型层级让操作类型安全 | `CreoModel`(concrete fallback) → `CreoSolid`(abstract, Regenerate 等实体操作) → `CreoPart`/`CreoAssembly`;`CreoDrawing`(2D) |

## 2. 五条横切命名规则(R1–R5,改方法必读)

### R1 — 找不到的策略:**query→null;command→throw**
- **状态查询/枚举**(`Current`/`Find`/`First`/`Origin`/`CommonName`/`GetDisplayStatus`)→ `T?`(null=无)。
- **命令/动作**(`Retrieve`/`PickProgrammatic`/`Save`)→ 抛 `CreoException`。
- 这是**有意区分**(VB API/OTK 也这么分),**不要**为"统一"全改 null 或全抛。

### R2 — 存在性编码:**查询 null / 动作 bool**
- `GetDisplayStatus(name)→CreoLayerDisplayStatus?`(查询;layer 不存在 = null)。
- `Create/Delete/SetDisplayStatus/AddFeature/Erase/CopyTo/SetDesignated`→ `bool`(动作;`true`=本次确实发生,`false`=幂等/不存在的信号)。

### R3 — 不滥用 `Try` 前缀
- `Try` 仅给 .NET 习惯的 out-param 模式(`TryParse`-式)。
- best-effort 动作(无窗口=false 等)直接用名词性方法名 + `bool` 返回(`RepaintCurrent()`);严格版另起方法(`Repaint(int id)`,失败抛)。

### R4 — 导航属性 vs I/O 方法
- **纯导航门面**(无 Creo 调用):**属性**——`CreoSession.Models/Selection/Features/Windows`、`CreoModel.Parameters/Layers`。
- **带 Creo 往返的取值器**:**方法**——`Current()`/`First()`/`IsModified()`/`CommonName()`/`Origin()`。
- 依据:.NET Framework Design Guidelines"做 I/O 的取值器用方法",有据偏离 VB 的属性形态。

### R5 — 同名不同形由类型本身区分语境
- `CreoModel.IsModified()`(活查询=方法)vs `CreoParameter.IsModified`(快照字段=属性)。**由 R4 自然导出**,保留 + 文档说明,不强求统一。

## 3. 类型骨架

```
CreoSession                                            (根;Attach()/Dispose)
  ├─ Models    : CreoModels     ── Current()/Retrieve(name,type)
  ├─ Selections: CreoSelections ── Select/CreateModelItemSelection → CreoSelectionSet  (对齐 pfcBaseSession::Select/pfcSelect)
  ├─ Features  : CreoFeatures   ── First()/ExtractElementTree
  ├─ Windows   : CreoWindows    ── CurrentId/Repaint(id)/RepaintCurrent()
  ├─ Events    : CreoEvents     ── 单 handler Subscribe*(directory/save/group-ungroup-pre)
  └─ EventBus  : CreoEventBus   ── 多 handler On*(directory/save/group-ungroup-pre)

Selection 域(对齐 OTK):
  CreoSelections (facade,复数)
  CreoSelectionSet : IReadOnlyList<CreoSelection>, IDisposable     (owned 副本集)
  CreoSelection (sealed;对齐 pfcSelection)
      ├─ Label / SelItem:ICreoModelItem? / SelModel:CreoModel? / Id / RawTypeCode / TypeName
      ├─ Path:CreoComponentPath?  (装配组件路径,可经 CreateModelItemSelection 重建 selection)
      ├─ Highlight / UnHighlight / Display

CreoModel (concrete fallback, session-bound)           [Save/Erase/CopyTo/IsModified/Origin/CommonName]
  ├─ CreoSolid (concrete)      ── Regenerate()         [Part/Assembly 实体；未知 solid 类型的 fallback]
  │   ├─ CreoPart (sealed)
  │   └─ CreoAssembly (sealed)
  └─ CreoDrawing (sealed)                              [2D,无 Regenerate]
  AsSolid() : CreoSolid?

CreoModel 子门面
  ├─ Parameters : CreoParameters    [Find/List/ListPage/Set/Delete/Reset/IsDesignated/SetDesignated]
  ├─ Layers     : CreoLayers        [ListNames/Create/Delete/GetDisplayStatus/SetDisplayStatus/AddFeature]
  ├─ GetPrincipalUnitSystem() / UnitSystems()
  └─ Drawing 方法: GetSheetsCount/GetCurrentSheetIndex/GetViewsCount/ListViews 等

ModelItem 域(ICreoModelItem + 12 个主要衍生类,Items/ 目录)
  ICreoModelItem ── Type:CreoModelItemType / Id (DHandle=属性, OHandle=方法)
    ├─ CreoFeature / CreoLayer / CreoNote / CreoDimension / CreoRefDimension   (DHandle 系)
    └─ CreoAxis / CreoCsys / CreoCurve / CreoEdge / CreoPoint / CreoSurface / CreoQuilt  (OHandle 系)
  不下沉:CreoParameter (独立 name-keyed 值快照,对应 OTK pfcParameterOwner)

Geometry 值类型(纯 DATA,Geometry/ 目录)
  CreoVec3 / CreoMat4 / CreoPlane / CreoLine3 / CreoCoordSystem    (几何代数)

CreoParamValue (readonly struct)   Kind + typed accessor(OfString/OfDouble/OfInt/OfBool / AsXxx)
集合一律 IReadOnlyList<T> 快照(对应 VB IpfcXxxs / OTK xtcsequence)
```

## 4. 验收标准(改 SDK 必须满足)

- **A 类**(无堆内存动作:Save/Regenerate/Erase/Set/Window* 等)→ 真机 `rc=0` 验证。
- **B 类**(带 DATA/LIVE/BORROWED 内存:任何 copy-out 串/数组/Selection/Elemtree)→ 泄漏检测测试 `balanced=0`(iter≥1000)。**`rc=0` 时 native 层无可观测泄漏**。
- 跑法：`user_initialize` 本身只完成 attach/host 启动，不自动执行 SDK 测试。A 类经 RealCreoIntegration/CreoGate 或显式 `CTK_APP_RUN_COMMAND` 验证；B 类必须运行专门的泄漏检测测试并检查 `balanced=0`。新增/改 B 类能力通过验证后再更新绑定契约表 `confirmed:true`。

## 5. 常见反模式(踩这条 = PR 退回)

- ❌ `Try` 前缀给 best-effort 动作(违 R3)。
- ❌ 纯查询型动作返回 bool 当存在性(违 R1/R2;query 应 null,action 才 bool)。
- ❌ 在主 L3 面暴露 `IntPtr/NativeMethods`(违 P5;走 `Sdk.Advanced`)。
- ❌ 把 `Regenerate` 加回 `CreoModel`(违 P6;只在 `CreoSolid`)。
- ❌ `public new T()` 公开构造(违 P3;ctor 必须 internal/protected)。
- ❌ B 类能力只用 `rc=0` 验收(违验收标准;必须过泄漏检测测试 balanced=0)。
- ❌ 新增 `ICreoModelItem` 衍生类不实现 Type/Id 强类型(出口字符串化会破 Selection.Item 强类型契约)。
- ❌ 把 `CreoParameter` 下沉到 ModelItem 抽象(OTK ModelItem 11 类不含 Parameter,独立 `pfcParameterOwner` 接口)。

## 6. 加新能力的标准动作(checklist)

1. 是 A 类还是 B 类?(无堆出参=A;`Type**`/`ProArray`/句柄=B)
2. B 类:先查绑定契约表;A 类:看是否已在 `Generated/**/Methods/Pro*.g.cs`。
3. 落到哪个类?对照"类型骨架"(实体操作 → `CreoSolid`;DATA 取值器 → 方法 + 返回 `T?`)。
4. 命名按 R1–R5 自检。
5. 写非 Creo 单测,经 `FakeCreoNative`。
6. 改 native?重建 `build_native.cmd CreoToolkit.NativeHost Release` 后跑泄漏检测测试(B 类必须;A 类真机 `rc=0` 验证)。
7. 更新绑定契约表(B 类通过验证后)。

## 7. 模型域能力与前置条件

| 域 | 主要入口 | 关键前置条件 / 返回策略 |
|---|---|---|
| 通用模型 | `CreoModel.Save/Erase/CopyTo/IsModified/GetOrigin/GetCommonName` | 对象与创建它的 session 绑定；session 关闭后 LIVE 操作抛 `CreoSessionClosedException` |
| 实体 | `CreoSolid.Regenerate/Display/GetOutline/GetMassProperty/ListFailedFeatures` | 只适用于 Part/Assembly；Drawing 不暴露 Regenerate |
| 装配 | `ListComponents/GetPathMdl/GetPathTransform/GetSkeleton` | component path 最多 25 级；无效路径查询返回 null |
| 装配 snapshot | `GetSnapshotTransforms/CreateSnapshot/DeleteSnapshot` | native API 没有 assembly 入参，因此 receiver 必须是当前活动装配；写操作错对象抛异常。真机上“不存在 snapshot”可能以 GeneralError 抛出，而不是 null |
| 工程图 | `ListViews/ListTables/ListDimensions/GetDimensionAttachments/CreateDimension` | dimension/item 必须属于同 session；跨 drawing 同 id 会先做归属校验；部分不可应用查询返回 null |
| 参数 | `Find/List/ListPage/Set/Delete/Reset/SetDesignated` | query 找不到通常 null/空列表；写动作失败抛 `CreoException`，幂等动作按具体方法返回 bool |
| 特征 | `List/Delete/ExtractElementTree/ExtractElementTreeDeep/CreateDatumPlane/RedefineHoleDiameter` | feature/model/reference 必须属于同 session；删除后原 feature 对象失效；创建/重定义是写操作，失败抛异常 |

API 的 XML 注释是单方法前置条件真源；本表只给出横向规则。任何标注“未经真机验证”或所有权未确认的 API，不能仅凭 `rc=0` 宣称完成兼容验证。

## 8. 事件与回调

- `CreoSession.Events` 是单 handler 门面：同一 notification type 只允许一个订阅，重复订阅抛 `InvalidOperationException`。
- `CreoSession.EventBus` 是多 handler 聚合层：同一 native notification 只注册一次，每个 `IDisposable` token 只移除自己的 handler；最后一个 handler 移除时释放 native 订阅。
- 当前公开事件为 `DirectoryChanged`、`ModelSavePre`、`GroupUngroupPre`。
- callback 在 Creo 主线程执行。普通 handler 异常会被隔离并记录；`GroupUngroupPre` handler 异常默认按 Allow 处理，避免误拦 UI。
- 订阅对象由 session 跟踪；显式 Dispose 可提前取消，遗漏 Dispose 时 `CreoSession.Dispose` 负责最终清理，先 unset Creo 回调，再释放 GC root。

## 9. 异常与“不存在”规则的例外

总原则仍是 query→null、command→throw，但 Toolkit 返回码并非所有域都能无损映射为“不存在”。已知例外必须以单方法 XML 注释为准，例如：

- `CreoAssembly.GetSnapshotTransforms`：不存在的 snapshot 在部分真机版本可能抛 `CreoException`。
- Drawing 尺寸 attachment：不适用尺寸通常返回 null；跨 session 入参抛 `InvalidOperationException`。
- 同步拓扑下从非 Creo 主线程调用 SDK，会抛 `InvalidOperationException`，不会自动 marshal 到主线程。

调用方不得把“没有抛异常”当作 UI 命令成功证据；App command dispatcher 另有 0/1/2 返回码契约，见 `docs/creo-host-loading.md`。
