# CreoToolkit event 命名速查表

> 本文维护已发出的结构化日志 `event` 键清单。

## 命名规则速记

**格式：`domain.subject.action`** 三段，全小写点分。
- **稳定**：一旦发出就**不应改名**（AI 聚类、告警规则、未来 i18n 都靠它）
- **可识别 begin/ok/fail**：长操作三段；瞬时操作单一即可
- **subject 用名词单数**：`command` 而非 `commands`
- **domain 是仓内概念域**：host / command / menu / lifecycle / option / route / resource / model / layer / feature / parameter / session / item / assembly
- **业务侧自由扩展**：如 `myapp.export.pdf.ok`（按 module 区分）

## 已发出 event 清单

### Host / Bootstrap 域（CreoAppHost.cs / Bootstrap.cs / CreoLog.cs）
- `host.init.begin` / `host.init.ok`
- `host.env.resolved`（env 解析快照）
- `host.preload.fail`（预加载失败）
- `session.start`（Bootstrap.cs；Serilog 首行会话锚点，props 带 `pid`/`machine`/`user`/`cwd`）
- `config.env.deprecated`（CreoLog.cs；旧 env 命中一次性 WARN）
- ~~`host.init.fail`~~ / ~~`host.session.attach.ok` / `host.session.attach.fail`~~ / ~~`host.app.load.ok` / `host.app.load.fail`~~ / ~~`host.preload.ok`~~ / ~~`host.load.ok` / `host.load.fail`~~ / ~~`host.diagnostic.cmd.ok` / `host.diagnostic.cmd.fail`~~（代码中已无发射点，历史事件已停用）

### Command 域（CreoAppHost.cs）
- `command.action.add.try`（注册尝试；原 `.ok` / `.fail` 已不发）
- `command.designate.ok`
- `command.dispatch.begin` / `command.dispatch.ok` / `command.dispatch.fail`
- `command.dispatch.echo.fail`（消息回显失败）
- `command.dispose.step.fail`（dispose 单步失败）

### Menu / UI 域（CreoAppHost.cs）
- `menu.add.ok` / `menu.add.fail`
- `menu.sub.add.ok` / `menu.sub.add.fail`
- `menu.button.add.ok` / `menu.button.add.fail`
- `menu.check.add.ok` / `menu.check.add.fail`
- `menu.radio.add.ok` / `menu.radio.add.fail`
- `menu.ext.skip` / `menu.child.skip`（外观项失败分级降级跳过）
- `menu.registration.summary`（注册汇总）
- `lifecycle.init.begin` / `lifecycle.init.ok` / `lifecycle.skip`
- `option.add.ok` / `option.act.fail` / `option.val.fail`
- `messagebar.mirror.ok` / `messagebar.mirror.fail`
- `popup.notify.set.ok` / `popup.notify.fail`
- `ribbon.custom.ok` / `ribbon.custom.fail`

### Route 域（CreoToolkit.Agent，CreoCommandRouter.cs）
- `route.ok` / `route.fail` / `route.deny`

### Resource 域
- `resource.release.ok` / `resource.release.fail`（CreoReleaser.cs；props 带 `kind`=Elemtree/Selection/OwnedBlock）
- ~~`resource.selection.free.fail`~~（原 ctk_ops_selection.cpp 发射点已停用；owned 选择副本释放并入 `resource.release.*`，kind=Selection）

### App 业务域
- ~~`ctk.app.diagnostic`~~（已废弃；App 业务日志直接走上列 Menu / UI / Route 等具体事件键）

> **SDK 层（L3）发射机制**：SDK 不再直接写 JSONL。`CreoSdkLog` 转为 telemetry-only 门面，起 Activity 名 `domain.action` 经 `CreoTelemetry.SdkSource` 暴露给 ActivityListener / OpenTelemetry exporter；`.ok` / `.fail` 体现为 Activity 状态（`Ok` / `Error`），Trace / Warn 为 Activity event。下列 SDK 各域是 `domain.action` 语义键清单，是否落 JSONL 由 Host/App 侧 listener 决定。除下列域外，尚有 drawing / unit / unitsystem / curve / axis / edge / quilt 等只读 trace 域（完整清单待随后续版本补齐）。

### Model 域（SDK L3，CreoModels.cs）
- `model.load.ok` / `model.load.fail` / `model.load.miss`（path 失效返 null）
- `model.retrieve.ok` / `model.retrieve.fail`
- `model.init.ok` / `model.init.fail`
- `model.save.ok` / `model.save.fail`
- `model.regenerate.ok` / `model.regenerate.fail`
- `model.eraseall.ok` / `model.eraseall.fail`（props 带 `type`）
- `model.erasenotdisplayed.ok` / `model.erasenotdisplayed.fail`
- `model.retrievesimprep.ok` / `model.retrievesimprep.fail`（props 带 `simprep`）
- `model.erase.ok` / `model.erase.fail`（props 带 `erased`）
- `model.copyto.ok` / `model.copyto.fail`（props 带 `from/to/copied`）
- `model.cleanupdeps.ok` / `model.cleanupdeps.fail`
- `model.setlock.ok` / `model.setlock.fail`（props 带 `locked`）
- `model.backup.ok` / `model.backup.fail`（props 带 `dir`）
- `model.rename.ok` / `model.rename.fail`（props 带 `from/to/saveAfter`）
- `model.delete.ok` / `model.delete.fail`
- `model.display.ok` / `model.display.fail`

### Layer 域（SDK L3，CreoLayers.cs）
- `layer.create.ok` / `layer.create.fail`
- `layer.delete.ok` / `layer.delete.fail`
- `layer.setdisplaystatus.ok` / `layer.setdisplaystatus.fail`
- `layer.addfeature.ok` / `layer.addfeature.fail`

### Feature 域（SDK L3，CreoFeatures.cs）
- `feature.delete.ok` / `feature.delete.fail`
- `feature.elemtree_extract.ok` / `feature.elemtree_extract.fail`（长 LIVE 元素树抽取，props 带 `nodes` 节点数）

### Parameter 域（SDK L3，CreoParameters.cs）
- `parameter.set.ok` / `parameter.set.fail`（props 带 `kind`）
- `parameter.delete.ok` / `parameter.delete.fail`
- `parameter.reset.ok` / `parameter.reset.fail`
- `parameter.setdesignated.ok` / `parameter.setdesignated.fail`

### Session 域（SDK L3，CreoSession.cs）
- `session.attach.ok` / `session.attach.fail`（props 带 `tid`）
- `session.dispose.ok` / `session.dispose.fail`（props 带 `resources` / `failed`）

### Item 域（SDK L3，CreoModelItem.cs；所有 12 个 ModelItem 子类共享）
- `item.setname.ok` / `item.setname.fail`
- `item.clearusername.ok` / `item.clearusername.fail`
- props 带 `{model, type, id}` 三元组定位

### Assembly 域（SDK L3，CreoAssembly.cs）
- `assembly.explode.ok` / `assembly.explode.fail`
- `assembly.unexplode.ok` / `assembly.unexplode.fail`

### Drawing 域（SDK L3，CreoDrawing.cs；只读 trace）
- `drawing.list-views`（视图 DATA 快照列举；props 带 `drawing/count/skipped`）
- `drawing.dtlentity-list` / `drawing.dtlentity-data-get`（draft entity 只读，reason 后缀在源码常量表）

### Geometry 域（SDK L3，几何代数 read 系列 spec · CreoNativeBridge.{Surface,Csys,Point,Items,Asmcomppath}.cs）

> 注：下列 4 域 5 个 L3 read API 覆盖 Surface / Csys / Point / Assembly 几何读取。

> 4 域 5 个 L3 read API + Bridge 18 条 null 早退 trace（9 unique reason 后缀）。production 默认 warn 阈值自动屏蔽。

| event 主键 | reason 后缀 | 触发场景 |
|---|---|---|
| `surface.typeget` | `owner_unresolved` / `init_notfound` / `type_failed` | `CreoSurface.GetSurfaceType()` null 早退 |
| `surface.planedataget` | `owner_unresolved` / `init_notfound` / `type_failed` / `not_plane` / `eval_failed` | `CreoSurface.GetPlane()` null 早退 |
| `csys.matrixget` | `owner_unresolved` / `init_notfound` / `dataget_notfound` | `CreoCsys.GetMatrix()` null 早退 |
| `point.coordget` | `owner_unresolved` / `init_notfound` / `coordget_notfound` | `CreoPoint.GetCoord()` null 早退 |
| `assembly.pathmdlget` / `assembly.pathtransformget` | `path_init_failed` / `miss` | `CreoAssembly.GetPathTransform()` + `AssemblyPathMdlGet` null 早退 |

> SDK 各域统一走 `CreoSdkLog` telemetry 门面，Activity 打 `ctk.domain` / `ctk.action` tag（不再写 `module` / `layer` JSONL 字段）。
> 业务侧扩 plugin 自动走同一 helper，命名稳定性自动有保障。

## 拦截型事件契约

拦截型 PRE 事件（当前仅 `PRO_GROUP_UNGROUP_PRE`，`CreoEvents.SubscribeGroupUngroupPre` / `CreoEventBus.OnGroupUngroupPre`）在 wrapper 层的错误映射与 DATA context 三态语义与其它非拦截事件不同，登记如下：

- **handler 抛异常** → wrapper 返 `PRO_TK_NO_ERROR`（默认 Allow）+ warn 日志 `notify.handler.exception`（props 带 `type`）。
  **非 PoC 时期的 `PRO_TK_GENERAL_ERROR`**：PoC SEH 语义（返 GENERAL_ERROR）对非拦截事件（`DirectoryChanged` / `ModelSavePre`）无害，但对拦截型 PRE 会被 Creo 解读为拦截，导致 handler 异常时误拦截用户正常 ungroup 操作。故 `GroupUngroupPre` wrapper 特例覆盖为 Allow。
- **`context.IsTabledriven` 查询失败** → `bool?` 返 `null`；触发条件：local group（`ProGroupIsTabledriven` 返 `PRO_TK_BAD_CONTEXT`）/ 其它非 `NO_ERROR` rc 均映 null，不当整体失败。
- **`context.MemberFeatureIds` 收集失败** → 返空列表（`Array.Empty<int>()`）；handler 仍调，不当整体失败；`ProGroupFeaturesCollect` 输出 ProArray 无论 rc 如何、只要指针非零必经 `finally` 段 `ProArrayFree`。

## 加新 event 步骤

1. 选 event 名按 `domain.subject.action` 三段点分
2. msg 中文直写
3. props 加上下文（token / id / duration_ms / model 名等）
4. 长操作三段（begin/ok/fail）；瞬时单一即可
5. 业务命令内用 `Scope(corr)` 自动关联
6. 本文 §"已发出 event 清单" 对应域追加一行
