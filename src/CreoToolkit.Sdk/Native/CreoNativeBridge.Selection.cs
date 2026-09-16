using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- 选择 (BORROWED: 原 static 不碰, owned 副本集) ----

    /// <summary>ProSelect 交互式(对齐 pfcBaseSession::Select):弹 Creo UI, 阻塞主线程等用户点选,
    /// middle-click 结束返回。
    /// <para>owned-copy 出口:p_sel_array 归 BORROWED
    /// (库 static, ownership.yml element_copy 契约),逐条 ProSelectionCopy 拷出 owned 副本,
    /// 原数组不 free;每项读 <c>pro_model_item.owner</c> 反查 per-item ModelIdentity(交互选择可跨模型)。</para>
    /// <para>copy 对交互 selection 的 asmcomppath/view 等上下文完整性未真机验证(挂账真机场);
    /// owned 副本支撑 Highlight/Display 已由 programmatic 路径真机 smoke 佐证。</para></summary>
    public unsafe SelectResult Select(string optionKeywords, int maxNumSels)
    {
        var funcs = default(GNS.pro_sel_functions); // filter/action 全 NULL = 无过滤
        var optionPtr = Marshal.StringToHGlobalAnsi(optionKeywords);
        var selectionEnv = IntPtr.Zero;
        try
        {
            selectionEnv = CreateDefaultSelectionEnvIfNeeded(maxNumSels);
            var rc = G.ProSelect(optionPtr, maxNumSels, IntPtr.Zero, ref funcs,
                                 selectionEnv, IntPtr.Zero, out var selArr, out var nSels);
            // rc 分流按 ProSelection.h:493-500 官方契约:USER_ABORT(用户 Quit)/PICK_ABOVE
            // (上层菜单打断)是合法取消 → 归空;其余非成功 rc(E_DEADLOCK/BAD_INPUTS 等)
            // 是真错误必须抛,不许被 nSels==0 吞成"用户没选"
            if (IsInteractiveSelectCancellation(rc))
            {
                CreoSdkLog.Trace("selection", "select-interactive.cancelled",
                    new { optionKeywords, maxNumSels, rc = (int)rc });
                return EmptySelectResult();
            }
            EnsureInteractiveSelectSucceeded(rc);
            // NO_ERROR + 0 条 = 用户直接结束没选(ProSelection.h:465-467 明示合法)
            if (nSels == 0 || selArr == IntPtr.Zero)
                return EmptySelectResult();

            var ownedSels = new List<IntPtr>(nSels);
            var items = new List<SelectionItemData>(nSels);
            bool committed = false;
            try
            {
                for (int i = 0; i < nSels; i++)
                {
                    var ptr = Marshal.ReadIntPtr(selArr, i * IntPtr.Size);
                    if (ptr == IntPtr.Zero) continue;
                    var mi = default(GNS.pro_model_item);
                    var mrc = G.ProSelectionModelitemGet(ptr, ref mi);
                    if (mrc != GNS.ProErrors.PRO_TK_NO_ERROR)
                    {
                        // 读不出 modelitem 的项整体跳过(不 copy 不入 items):
                        // ownedSels 与 items 恒同步追加,Highlight(index) 与句柄 index 不错位
                        CreoSdkLog.Trace("selection", "select-interactive.modelitem_skip",
                            new { optionKeywords, index = i, rc = (int)mrc });
                        continue;
                    }
                    // copy 交接 sentinel:copy 成功到进 ownedSels 之间任何异常,
                    // 本地 catch 兜底释放,不依赖外层 finally(其只遍历 ownedSels)
                    IntPtr copy = IntPtr.Zero;
                    try
                    {
                        Check.Eval(nameof(G.ProSelectionCopy), G.ProSelectionCopy(ptr, out copy));
                        ownedSels.Add(copy);
                        copy = IntPtr.Zero;
                    }
                    catch
                    {
                        if (copy != IntPtr.Zero)
                        {
                            var c = copy;
                            _ = G.ProSelectionFree(ref c);
                        }
                        throw;
                    }

                    // per-item owner 反查(交互选择可跨模型);不可解析 → null(物化层降级 Item=null)
                    var owner = ResolveModelIdentity(mi.owner);
                    if (owner is null)
                        CreoSdkLog.Trace("selection", "select-interactive.owner_unresolved",
                            new { optionKeywords, index = i, id = mi.id, owner = mi.owner.ToInt64() });

                    // 装配组件路径抽取(ProSelectionAsmcomppathGet,DATA 化);part/顶层场景
                    // 或抽取失败 → null(不阻断本项,path 是增值信息非必需)
                    var path = TryExtractComponentPath(ptr, optionKeywords, i);

                    int rawType = (int)mi.type;
                    var itemType = Enum.IsDefined(typeof(CreoModelItemType), rawType)
                        ? (CreoModelItemType)rawType
                        : CreoModelItemType.Unknown;
                    items.Add(new SelectionItemData(
                        Index: items.Count,
                        Label: $"type={rawType},id={mi.id}",
                        ItemType: itemType,
                        ItemId: mi.id,
                        Owner: owner,
                        RawTypeCode: rawType,
                        Path: path));
                }

                var ownedRes = new OwnedSelectionsResource(this, ownedSels.ToArray());
                committed = true;
                CreoSdkLog.Trace("selection", "select-interactive.bridge",
                    new { optionKeywords, maxNumSels, count = items.Count });
                return new SelectResult(ownedRes, items);
            }
            finally
            {
                if (!committed)
                {
                    foreach (var p in ownedSels)
                    {
                        var tmp = p;
                        if (tmp != IntPtr.Zero)
                            _ = G.ProSelectionFree(ref tmp);
                    }
                }
            }
        }
        finally
        {
            FreeSelectionEnvIfAllocated(selectionEnv, optionKeywords, maxNumSels);
            Marshal.FreeHGlobal(optionPtr);
        }
    }

    internal static bool ShouldCreateDefaultSelectionEnv(int maxNumSels)
        => maxNumSels == -1 || maxNumSels > 1;

    internal static bool IsInteractiveSelectCancellation(GNS.ProErrors rc)
        => rc == GNS.ProErrors.PRO_TK_USER_ABORT
        || rc == GNS.ProErrors.PRO_TK_PICK_ABOVE;

    internal static void EnsureInteractiveSelectSucceeded(GNS.ProErrors rc)
    {
        if (IsInteractiveSelectCancellation(rc))
            return;

        Check.Eval(nameof(G.ProSelect), rc);
    }

    internal static GNS.ProSelectionEnvOption[] BuildDefaultSelectionEnvOptions()
    {
        const int enabled = 1;
        return new[]
        {
            new GNS.ProSelectionEnvOption
            {
                attribute = GNS.ProSelectionEnvAttr.PRO_SELECT_DONE_REQUIRED,
                value = enabled,
            },
            new GNS.ProSelectionEnvOption
            {
                attribute = GNS.ProSelectionEnvAttr.PRO_SELECT_BY_MENU_ALLOWED,
                value = enabled,
            },
            new GNS.ProSelectionEnvOption
            {
                attribute = GNS.ProSelectionEnvAttr.PRO_SELECT_BY_BOX_ALLOWED,
                value = enabled,
            },
        };
    }

    private static unsafe IntPtr CreateDefaultSelectionEnvIfNeeded(int maxNumSels)
    {
        if (!ShouldCreateDefaultSelectionEnv(maxNumSels))
            return IntPtr.Zero;

        var options = BuildDefaultSelectionEnvOptions();
        fixed (GNS.ProSelectionEnvOption* first = options)
        {
            Check.Eval(nameof(G.ProSelectionEnvAlloc),
                G.ProSelectionEnvAlloc(ref first[0], options.Length, out var selectionEnv));
            return selectionEnv;
        }
    }

    private static void FreeSelectionEnvIfAllocated(IntPtr selectionEnv, string optionKeywords, int maxNumSels)
    {
        if (selectionEnv == IntPtr.Zero)
            return;

        var rc = G.ProSelectionEnvFree(selectionEnv);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
        {
            CreoSdkLog.Warn("selection", "select-interactive.env_free_failed",
                "ProSelectionEnvFree failed during selection cleanup; original ProSelect result is preserved.",
                new { optionKeywords, maxNumSels, rc = (int)rc });
        }
    }

    // 取消/零选中的合法空结果(空 owned 集,Dispose 为空操作)
    private SelectResult EmptySelectResult()
        => new(new OwnedSelectionsResource(this, Array.Empty<IntPtr>()), Array.Empty<SelectionItemData>());

    /// <summary>从 selection 抽装配组件路径并 DATA 化(pro_comp_path{owner, id_table[25], table_num})。
    /// part/顶层场景(owner 空或 table_num≤0)、rc 失败、root 反查失败均返 null 并 trace。</summary>
    private unsafe ComponentPathData? TryExtractComponentPath(IntPtr selection, string option, int index)
    {
        var cp = default(GNS.pro_comp_path);
        var rc = G.ProSelectionAsmcomppathGet(selection, ref cp);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR || cp.owner == IntPtr.Zero || cp.table_num <= 0)
            return null; // 合法的"无路径"形态(part/顶层),不作错误处理
        if (cp.table_num > ComponentPathData.MaxDepth)
        {
            CreoSdkLog.Warn("selection", "select-interactive.path_depth_overflow",
                "comp_path table_num 超 native 定长上限,路径丢弃",
                new { option, index, tableNum = cp.table_num });
            return null;
        }
        var root = ResolveModelIdentity(cp.owner);
        if (root is not { } r)
        {
            CreoSdkLog.Trace("selection", "select-interactive.path_root_unresolved",
                new { option, index, owner = cp.owner.ToInt64() });
            return null;
        }
        var ids = new int[cp.table_num];
        for (int k = 0; k < ids.Length; k++)
            ids[k] = cp.comp_id_table[k];
        return new ComponentPathData(r, ids);
    }

    /// <summary>由 (item, path) 创建 selection(对齐 pfcSelect 全局工厂 CreateModelItemSelection;
    /// ProSelection 与 item+path 双向可逆)。
    /// ProModelitemInit 构 item + ProAsmcomppathInit 构 path + ProSelectionAlloc(组合表:
    /// path=NULL 仅限 part,经 GeneratedHelpers IntPtr 重载传真 NULL)→ 即刻 ProSelectionCopy
    /// 出 owned 副本,src 释放。</summary>
    public SelectResult CreateModelItemSelection(ItemRef item, ComponentPathData? path)
    {
        if (!TryResolveMdl(item.Model, out var ownerMdl))
            throw new CreoException("ProMdlnameInit", ProError.NotFound,
                $"item owner 模型 '{item.Model.Name}' 不在会话中。");

        var mitem = default(GNS.pro_model_item);
        Check.Eval(nameof(G.ProModelitemInit),
            G.ProModelitemInit(ownerMdl, item.Id, (GNS.pro_obj_types)(int)item.Type, ref mitem));

        var ownedSels = new List<IntPtr>(capacity: 1);
        var items = new List<SelectionItemData>(capacity: 1);
        bool committed = false;
        try
        {
            IntPtr src = IntPtr.Zero;
            IntPtr copy = IntPtr.Zero;
            bool copyAdded = false;
            try
            {
                if (path is null)
                {
                    // 组合表 (NULL, !NULL):part/顶层场景,必须真 NULL 指针(零结构体 ≠ NULL)
                    Check.Eval(nameof(GeneratedHelpers.ProSelectionAlloc),
                        GeneratedHelpers.ProSelectionAlloc(IntPtr.Zero, ref mitem, out src));
                }
                else
                {
                    if (!TryResolveMdl(path.Value.Root, out var rootMdl))
                        throw new CreoException("ProMdlnameInit", ProError.NotFound,
                            $"path root 模型 '{path.Value.Root.Name}' 不在会话中。");
                    var ids = path.Value.ComponentIds;
                    if (ids.Count is 0 or > ComponentPathData.MaxDepth)
                        throw new ArgumentOutOfRangeException(nameof(path), ids.Count,
                            $"component id 链长度必须在 1..{ComponentPathData.MaxDepth}(native comp_id_table 定长)");
                    var tab = new int[ComponentPathData.MaxDepth];
                    for (int k = 0; k < ids.Count; k++)
                        tab[k] = ids[k];
                    var cp = default(GNS.pro_comp_path);
                    Check.Eval(nameof(G.ProAsmcomppathInit),
                        G.ProAsmcomppathInit(rootMdl, tab, ids.Count, ref cp));
                    Check.Eval(nameof(G.ProSelectionAlloc),
                        G.ProSelectionAlloc(ref cp, ref mitem, out src));
                }

                Check.Eval(nameof(G.ProSelectionCopy), G.ProSelectionCopy(src, out copy));
                ownedSels.Add(copy);
                copy = IntPtr.Zero;
                copyAdded = true;

                int rawType = (int)mitem.type;
                var itemType = Enum.IsDefined(typeof(CreoModelItemType), rawType)
                    ? (CreoModelItemType)rawType
                    : CreoModelItemType.Unknown;
                items.Add(new SelectionItemData(
                    Index: 0,
                    Label: $"type={rawType},id={mitem.id}",
                    ItemType: itemType,
                    ItemId: mitem.id,
                    Owner: item.Model,
                    RawTypeCode: rawType,
                    Path: path));
            }
            catch
            {
                if (copy != IntPtr.Zero)
                {
                    var c = copy;
                    _ = G.ProSelectionFree(ref c);
                }
                throw;
            }
            finally
            {
                if (src != IntPtr.Zero)
                {
                    int srcFreeRc = (int)G.ProSelectionFree(ref src);
                    if (srcFreeRc != 0)
                    {
                        CreoSdkLog.Warn("selection", "create.src.free.fail", "源 selection 释放失败。",
                            new { rc = srcFreeRc, op = "ProSelectionFree" });
                        if (copyAdded)
                            throw new CreoException("ProSelectionFree(src)", (ProError)srcFreeRc,
                                "源 selection 释放失败,owned 副本可能受影响。");
                    }
                }
            }

            var owned = new OwnedSelectionsResource(this, ownedSels.ToArray());
            committed = true;
            CreoSdkLog.Trace("selection", "selection-create.bridge",
                new { model = item.Model.Name, type = (int)item.Type, id = item.Id, hasPath = path is not null });
            return new SelectResult(owned, items);
        }
        finally
        {
            if (!committed)
            {
                foreach (var p in ownedSels)
                {
                    var tmp = p;
                    if (tmp != IntPtr.Zero)
                        _ = G.ProSelectionFree(ref tmp);
                }
            }
        }
    }

    /// <summary>[Obsolete] 空列表的模型自身 selection 兼容 seam：
    /// <c>ProMdlToModelitem + ProSelectionAlloc(顶层空表 path)</c> →
    /// <c>ProSelectionCopy</c> 出 owned 副本 → src 释放。非空列表防御性拒绝
    /// (public alias 同样拒绝非空,与本 seam 行为一致,不转发交互式 Select)。
    /// <para>迁移路径:交互多选走 <see cref="Select"/>;定向按 (item, path) 构 selection 走
    /// <see cref="CreateModelItemSelection"/>。</para></summary>
    [Obsolete("已废弃：改用 Select(optionKeywords) 或 CreateModelItemSelection(item,path)；本 seam 仅接受空列表的模型自身兼容路径。")]
    public SelectResult SelectProgrammatic(ModelIdentity model, IReadOnlyList<string> itemRefs)
    {
        ThrowUtil.IfNull(itemRefs);
        if (itemRefs.Count > 0)
            throw new NotSupportedException(
                "SelectProgrammatic 未定义字符串 itemRefs 解析语法；非空引用不可静默忽略。");

        if (!TryResolveMdl(model, out var mdl))
            throw new CreoException("ProMdlnameInit", ProError.NotFound,
                $"模型 '{model.Name}' 不在会话中。");

        var ownedSels = new List<IntPtr>(capacity: 1);
        var items = new List<SelectionItemData>(capacity: 1);
        bool committed = false;
        try
        {
            IntPtr src = IntPtr.Zero;
            IntPtr copy = IntPtr.Zero;
            bool copyAdded = false;
            try
            {
                // ProMdlToModelitem + 顶层空 comp_path 组合 → 整模型 ProSelection(FeatureCreate.AllocModelSelection 同款)
                var mi = default(GNS.pro_model_item);
                Check.Eval(nameof(G.ProMdlToModelitem), G.ProMdlToModelitem(mdl, ref mi));
                var path = default(GNS.pro_comp_path);
                path.owner = mdl;
                path.table_num = 0;
                Check.Eval(nameof(G.ProSelectionAlloc), G.ProSelectionAlloc(ref path, ref mi, out src));

                Check.Eval(nameof(G.ProSelectionCopy), G.ProSelectionCopy(src, out copy));
                ownedSels.Add(copy);
                copy = IntPtr.Zero;
                copyAdded = true;

                int rawType = (int)mi.type;
                var itemType = Enum.IsDefined(typeof(CreoModelItemType), rawType)
                    ? (CreoModelItemType)rawType
                    : CreoModelItemType.Unknown;
                items.Add(new SelectionItemData(
                    Index: 0,
                    Label: $"model={model.Name},type={rawType},id={mi.id}",
                    ItemType: itemType,
                    ItemId: mi.id,
                    Owner: model,
                    RawTypeCode: rawType,
                    Path: null));
            }
            catch
            {
                if (copy != IntPtr.Zero)
                {
                    var c = copy;
                    _ = G.ProSelectionFree(ref c);
                }
                throw;
            }
            finally
            {
                if (src != IntPtr.Zero)
                {
                    int srcFreeRc = (int)G.ProSelectionFree(ref src);
                    if (srcFreeRc != 0)
                    {
                        CreoSdkLog.Warn("selection", "programmatic.src.free.fail",
                            "源 selection 释放失败。",
                            new { rc = srcFreeRc, op = "ProSelectionFree" });
                        if (copyAdded)
                            throw new CreoException("ProSelectionFree(src)", (ProError)srcFreeRc,
                                "源 selection 释放失败,owned 副本可能受影响。");
                    }
                }
            }

            var owned = new OwnedSelectionsResource(this, ownedSels.ToArray());
            committed = true;
            CreoSdkLog.Trace("selection", "select-programmatic.bridge",
                new { model = model.Name, type = model.Type.ToString(), itemRefsCount = itemRefs.Count });
            return new SelectResult(owned, items);
        }
        finally
        {
            if (!committed)
            {
                foreach (var p in ownedSels)
                {
                    var tmp = p;
                    if (tmp != IntPtr.Zero)
                        _ = G.ProSelectionFree(ref tmp);
                }
            }
        }
    }

    public void SelectionHighlight(INativeResource set, int index)
    {
        set = UnwrapResource(set);
        if (set is not OwnedSelectionsResource res)
            throw new ArgumentException("set 不是本桥产生的 owned selections 资源。", nameof(set));
        var raw = res.GetRaw(index);
        Check.Eval(nameof(G.ProSelectionHighlight), G.ProSelectionHighlight(raw, GNS.ProColortype.PRO_COLOR_HIGHLITE));
    }

    public void SelectionUnhighlight(INativeResource set, int index)
    {
        set = UnwrapResource(set);
        if (set is not OwnedSelectionsResource res)
            throw new ArgumentException("set 不是本桥产生的 owned selections 资源。", nameof(set));
        Check.Eval(nameof(G.ProSelectionUnhighlight), G.ProSelectionUnhighlight(res.GetRaw(index)));
    }

    public void SelectionDisplay(INativeResource set, int index)
    {
        set = UnwrapResource(set);
        if (set is not OwnedSelectionsResource res)
            throw new ArgumentException("set 不是本桥产生的 owned selections 资源。", nameof(set));
        Check.Eval(nameof(G.ProSelectionDisplay), G.ProSelectionDisplay(res.GetRaw(index)));
    }

    // owned 选择副本集(BORROWED 三态)。每份 ProSelectionCopy 拷出的副本包成一个 owning
    // CreoSelectionHandle,与 elemtree 同纪律:Dispose 为确定性主路径,SafeHandle 自带 finalizer 兜底——
    // 漏 Dispose 时各句柄 finalizer 触发释放,经 CreoReleaseGate 在非主线程入队、主线程 pump 排干。
    // per-element 句柄(而非整批一个句柄):SafeHandle 本就一对一裸指针,逐副本各拥独立 GC 兜底,
    // 零额外分配(指针即句柄),精确镜像 elemtree 的 CreoElemtreeHandle 形态。
    internal sealed class OwnedSelectionsResource : INativeResource
    {
        private readonly CreoNativeBridge _bridge;
        private CreoSelectionHandle[]? _handles;
        private int _disposed;

        internal OwnedSelectionsResource(CreoNativeBridge bridge, IntPtr[] sels)
        {
            _bridge = bridge;
            // 全成功才接管所有权:阶段一仅分配句柄对象(可抛 OOM,但尚未持有任何指针,
            // free 责任仍全在调用方的 !committed finally);阶段二统一 AssignCopy——
            // SetHandle 纯字段赋值不抛,交接原子完成。任一时刻每个指针的 free 责任方唯一,无双释窗口。
            var handles = new CreoSelectionHandle[sels.Length];
            for (int i = 0; i < handles.Length; i++)
                handles[i] = new CreoSelectionHandle();
            for (int i = 0; i < handles.Length; i++)
                handles[i].AssignCopy(sels[i]); // Zero 保持无效,释放为空操作
            _handles = handles;
        }

        public int Count => Volatile.Read(ref _handles)?.Length ?? 0;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        internal IntPtr GetRaw(int index)
        {
            ThrowUtil.IfDisposed(Volatile.Read(ref _disposed) != 0, this);
            var handles = Volatile.Read(ref _handles)
                ?? throw new ObjectDisposedException(nameof(OwnedSelectionsResource));
            if ((uint)index >= (uint)handles.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            var handle = handles[index];
            if (handle.IsClosed || handle.IsInvalid)
                throw new ObjectDisposedException(nameof(OwnedSelectionsResource));
            return handle.DangerousGetHandle();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            var handles = Interlocked.Exchange(ref _handles, null);
            if (handles is null)
                return;

            // 逐句柄 Dispose:经 SafeHandle → CreoReleaseGate 分派(主线程直释 / 跨线程入队),
            // 释放后句柄自身归零(CreoSafeHandle 既有语义)。释放失败的 rc 日志由 DirectReleaser 承担。
            foreach (var handle in handles)
                handle.Dispose();
        }
    }
}
