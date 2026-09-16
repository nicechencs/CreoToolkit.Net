using System.Diagnostics;
using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Interop.Diagnostics;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Events;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- TestNotify: ProNotificationSet 直绑 ----
    // 直绑头文件优先;零新 native code。callback delegate 经 _notifyRegistry GC root
    // (复用 CallbackRegistry 模板,与 _visitRegistry 平行) + Marshal.GetFunctionPointerForDelegate。
    // SEH 隔离: callback 内 try/catch, 绝不让 C# 异常飞回 native; 异常吞掉返 PRO_TK_GENERAL_ERROR。
    // 同 type 守卫: SDK 层 Dictionary 跟踪已订阅 type, 重复抛 InvalidOperationException
    // (C 原生 ProNotificationSet 覆盖语义会让旧 callback 静默失效,易踩坑)。

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate GNS.ProErrors ProDirectoryChangePostActionDelegate(IntPtr newPath);

    // 真 Creo 实测修正:原计划用 PRO_MDL_SAVE_PRE + Action<ModelIdentity>(BORROWED ProMdl 入参)
    // 实测 Creo 4 已不触发(Pro Toolkit doc: DEPRECATED since Creo 3.0, 替代为 PRO_MODEL_SAVE_PRE)。
    // 新签名 ProModelSavePreAction(ProPath r_model_path)入参变 wchar_t* 路径,跟 DirectoryChanged 同形,
    // 无 BORROWED 解码需求;BORROWED ProMdl 入参解码范式留给真带 handle 入参的事件(如 PRO_FEATURE_REGEN_POST)。
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate GNS.ProErrors ProModelSavePreActionDelegate(IntPtr pathPtr);

    private readonly CallbackRegistry _notifyRegistry = new();
    private readonly Dictionary<GNS.pro_notify_type, Delegate> _notifySubscriptions = new();
    private readonly object _notifyGate = new();

    public INativeResource NotifySubscribeDirectoryChanged(Action<string> handler)
    {
        ThrowUtil.IfNull(handler);
        const GNS.pro_notify_type kType = GNS.pro_notify_type.PRO_DIRECTORY_CHANGE_POST;

        ProDirectoryChangePostActionDelegate cb = (newPath) =>
        {
            try
            {
                // D7-EXEMPT: Pro Toolkit callback 入参,BORROWED 指针(Pro 自管释放),不调 ProWstringFree。
                var path = newPath == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringUni(newPath) ?? string.Empty);
                handler(path);
                return GNS.ProErrors.PRO_TK_NO_ERROR;
            }
            catch
            {
                // SEH 隔离:绝不让 C# 异常飞回 native,吞掉返通用错(callback 设计契约 — 错误码通过返值传)
                return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            }
        };

        lock (_notifyGate)
        {
            if (_notifySubscriptions.ContainsKey(kType))
                throw new InvalidOperationException(
                    $"事件 {kType} 已订阅(同 type 只允许一个 handler;先 Dispose 旧订阅再 Subscribe)");

            var ptr = _notifyRegistry.Register(cb);
            try
            {
                Check.Eval(nameof(G.ProNotificationSet), G.ProNotificationSet(kType, ptr));
            }
            catch
            {
                _notifyRegistry.Unregister(cb);
                throw;
            }
            _notifySubscriptions[kType] = cb;
        }

        return new NotifySubscription(this, kType, cb);
    }

    public INativeResource NotifySubscribeModelSavePre(Action<string> handler)
    {
        ThrowUtil.IfNull(handler);
        const GNS.pro_notify_type kType = GNS.pro_notify_type.PRO_MODEL_SAVE_PRE;

        ProModelSavePreActionDelegate cb = (pathPtr) =>
        {
            try
            {
                // D7-EXEMPT: Pro Toolkit callback 入参,BORROWED 指针(Pro 自管释放),不调 ProWstringFree。
                var path = pathPtr == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringUni(pathPtr) ?? string.Empty);
                handler(path);
                return GNS.ProErrors.PRO_TK_NO_ERROR;
            }
            catch
            {
                // SEH 隔离:handler 异常不能飞回 native,吞掉返通用错。
                return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            }
        };

        lock (_notifyGate)
        {
            if (_notifySubscriptions.ContainsKey(kType))
                throw new InvalidOperationException(
                    $"事件 {kType} 已订阅(同 type 只允许一个 handler;先 Dispose 旧订阅再 Subscribe)");

            var ptr = _notifyRegistry.Register(cb);
            try
            {
                Check.Eval(nameof(G.ProNotificationSet), G.ProNotificationSet(kType, ptr));
            }
            catch
            {
                _notifyRegistry.Unregister(cb);
                throw;
            }
            _notifySubscriptions[kType] = cb;
        }

        return new NotifySubscription(this, kType, cb);
    }

    private bool TryNotifyUnsubscribe(GNS.pro_notify_type type, Delegate cb)
    {
        lock (_notifyGate)
        {
            if (!_notifySubscriptions.TryGetValue(type, out var current) || !ReferenceEquals(current, cb))
                return true; // 已被成功注销的幂等重试。
        }

        // native call 在锁外，避免回调重入时持锁。失败时字典与 GC root 都必须保留。
        GNS.ProErrors rc;
        try
        {
            rc = G.ProNotificationUnset(type);
        }
        catch (Exception ex)
        {
            CreoSdkLog.Warn("notify", "unset.exception",
                ex.Message, new { type = type.ToString(), exception = ex.GetType().FullName });
            return false;
        }

        if (rc is not (GNS.ProErrors.PRO_TK_NO_ERROR
            or GNS.ProErrors.PRO_TK_E_NOT_FOUND
            or GNS.ProErrors.PRO_TK_NOT_EXIST))
        {
            CreoSdkLog.Warn("notify", "unset.failed",
                "native 仍可能持有 callback；保留 GC root 等待重试。",
                new { type = type.ToString(), error = (int)rc });
            return false;
        }

        lock (_notifyGate)
        {
            if (!_notifySubscriptions.TryGetValue(type, out var current) || !ReferenceEquals(current, cb))
                return true;

            _notifySubscriptions.Remove(type);
            _notifyRegistry.Unregister(cb);
            return true;
        }
    }

    private void ReleaseNotifyAfterNativeTermination(GNS.pro_notify_type type, Delegate cb)
    {
        lock (_notifyGate)
        {
            if (!_notifySubscriptions.TryGetValue(type, out var current) || !ReferenceEquals(current, cb))
                return;

            // 仅在宿主已证明 native 运行时不会再回调后合法；本路径刻意不做任何 Creo API 调用。
            _notifySubscriptions.Remove(type);
            _notifyRegistry.Unregister(cb);
        }
    }

    private sealed class NotifySubscription : INativeResource, ISessionCloseAwareNativeResource
    {
        private readonly RetriableNativeSubscription _inner;

        public NotifySubscription(CreoNativeBridge bridge, GNS.pro_notify_type type, Delegate callback)
        {
            _inner = new RetriableNativeSubscription(
                () => bridge.TryNotifyUnsubscribe(type, callback),
                () => bridge.ReleaseNotifyAfterNativeTermination(type, callback));
        }

        public bool IsDisposed => _inner.IsDisposed;

        public void Dispose() => _inner.Dispose();

        public void MarkSessionClosed() => _inner.MarkSessionClosed();
    }

    // GroupUngroupPre — 真 native bridge:
    // callback wrapper SEH 隔离 + ref pro_model_item binding + ProArrayFree(feats != 0 必释)+
    // 拦截型 SEH 特例(handler/build 异常默认 Allow=NO_ERROR)+ Set 失败 Unregister 防 GC root 泄漏。
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate GNS.ProErrors ProGroupUngroupPreActionDelegate(IntPtr groupPtr);

    public INativeResource NotifySubscribeGroupUngroupPre(Func<CreoFeatureGroupContext, UngroupVote> handler)
    {
        ThrowUtil.IfNull(handler);
        const GNS.pro_notify_type kType = GNS.pro_notify_type.PRO_GROUP_UNGROUP_PRE;

        ProGroupUngroupPreActionDelegate cb = (groupPtr) =>
        {
            try
            {
                var context = BuildGroupContext(groupPtr);
                var vote = handler(context);
                return vote == UngroupVote.Block
                    ? GNS.ProErrors.PRO_TK_GENERAL_ERROR
                    : GNS.ProErrors.PRO_TK_NO_ERROR;
            }
            catch (Exception ex)
            {
                // 拦截型 SEH 特例:默认 Allow(NO_ERROR)防误拦截阻断 UI;
                // 异常以 telemetry event 暴露,listener 决定是否落盘/告警(SDK 不直接写日志)。
                using var act = CreoTelemetry.SdkSource.StartActivity("notify.handler.exception");
                if (act != null)
                {
                    act.SetTag("ctk.domain", "notify");
                    act.SetTag("ctk.action", "handler.exception");
                    act.SetTag("ctk.type", "PRO_GROUP_UNGROUP_PRE");
                    act.SetTag("exception.type", ex.GetType().FullName);
                    act.SetTag("exception.message", ex.Message);
                    act.SetStatus(ActivityStatusCode.Error, ex.Message);
                }
                return GNS.ProErrors.PRO_TK_NO_ERROR;
            }
        };

        lock (_notifyGate)
        {
            if (_notifySubscriptions.ContainsKey(kType))
                throw new InvalidOperationException(
                    $"事件 {kType} 已订阅(同 type 只允许一个 handler;先 Dispose 旧订阅再 Subscribe)");

            _notifyRegistry.Register(cb);
            try
            {
                Check.Eval(nameof(G.ProNotificationSet), G.ProNotificationSet(kType, Marshal.GetFunctionPointerForDelegate(cb)));
            }
            catch
            {
                // Set 失败必 Unregister,否则 GC root 泄漏
                _notifyRegistry.Unregister(cb);
                throw;
            }
            _notifySubscriptions[kType] = cb;
        }

        return new NotifySubscription(this, kType, cb);
    }

    private CreoFeatureGroupContext BuildGroupContext(IntPtr groupPtr)
    {
        if (groupPtr == IntPtr.Zero)
            throw new InvalidOperationException("group ptr is null");

        // 1. 拷 native pro_model_item 值,后续以 ref 重新传 native(generated binding 形态)
        var groupItem = Marshal.PtrToStructure<GNS.pro_model_item>(groupPtr);
        int groupId = groupItem.id;

        // 2. owner ProMdl:ProModelitemMdlGet(ref ..., out IntPtr)
        Check.Eval(nameof(G.ProModelitemMdlGet), G.ProModelitemMdlGet(ref groupItem, out IntPtr ownerMdl));

        // owner 名:ProMdlMdlnameGet(IntPtr, char[]) — 沿 Bridge.Model.cs 范式(180 长 buf + FromProName)
        var nameBuf = new char[180];
        Check.Eval(nameof(G.ProMdlMdlnameGet), G.ProMdlMdlnameGet(ownerMdl, nameBuf));
        string ownerName = FromProName(nameBuf);

        // owner 类型:ProMdlTypeGet → ProMdlType → CreoModelType(沿 FromProMdlType((int)ptype))
        Check.Eval(nameof(G.ProMdlTypeGet), G.ProMdlTypeGet(ownerMdl, out var ptype));
        CreoModelType ownerType = FromProMdlType((int)ptype);

        // 3. Name:UDF 经 ProUdfNameGet 取 name buf;local group / 取名失败 → null
        string? name = TryReadUdfName(ref groupItem);

        // 4. IsTabledriven:UDF 返实际值;local group(BAD_CONTEXT)或查询失败 → null
        bool? isTd = TryReadIsTabledriven(ref groupItem);

        // 5. MemberFeatureIds:ProGroupFeaturesCollect + ProArraySizeGet + 遍 + 必 ProArrayFree
        var memberIds = CollectMemberFeatureIds(ref groupItem);

        return new CreoFeatureGroupContext(groupId, name, ownerName, ownerType, isTd, memberIds);
    }

    // 取 name buf(非 instance),empty 映 null;rc != NO_ERROR 也映 null
    private static string? TryReadUdfName(ref GNS.pro_model_item groupItem)
    {
        var nameBuf = new char[32];
        var instanceBuf = new char[32];
        var rc = G.ProUdfNameGet(ref groupItem, nameBuf, instanceBuf);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR) return null;
        var s = FromProName(nameBuf);
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static bool? TryReadIsTabledriven(ref GNS.pro_model_item groupItem)
    {
        var rc = G.ProGroupIsTabledriven(ref groupItem, out GNS.ProBooleans td);
        if (rc == GNS.ProErrors.PRO_TK_BAD_CONTEXT) return null;  // local group
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR) return null;
        return td == GNS.ProBooleans.PRO_B_TRUE;
    }

    // 严格化:feats != IntPtr.Zero 必 ProArrayFree(包括 Collect 返错但已分配的路径)
    private static IReadOnlyList<int> CollectMemberFeatureIds(ref GNS.pro_model_item groupItem)
    {
        IntPtr feats = IntPtr.Zero;
        try
        {
            var rc = G.ProGroupFeaturesCollect(ref groupItem, out feats);
            if (rc != GNS.ProErrors.PRO_TK_NO_ERROR) return Array.Empty<int>();

            Check.Eval(nameof(G.ProArraySizeGet), G.ProArraySizeGet(feats, out int n));
            var result = new int[n];
            int elemSize = Marshal.SizeOf<GNS.pro_model_item>();
            for (int i = 0; i < n; i++)
            {
                var elemPtr = IntPtr.Add(feats, i * elemSize);
                var item = Marshal.PtrToStructure<GNS.pro_model_item>(elemPtr);
                result[i] = item.id;
            }
            return result;
        }
        catch { return Array.Empty<int>(); }
        finally
        {
            if (feats != IntPtr.Zero)
            {
                try { G.ProArrayFree(ref feats); } catch { /* 释放失败不致命 */ }
            }
        }
    }
}
