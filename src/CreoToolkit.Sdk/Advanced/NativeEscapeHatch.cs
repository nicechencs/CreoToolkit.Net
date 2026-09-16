using CreoToolkit.Sdk.Errors;

namespace CreoToolkit.Sdk.Advanced;

/// <summary>受控 native 逃生调用的结果，含 SDK 标准错误分类。</summary>
public readonly record struct NativeEscapeResult<T>(T Value, CreoResult Status);

/// <summary>
/// <see cref="CreoModel"/> 的 native 身份 DATA 快照。
/// 携带重建 <c>ProMdl</c> 句柄所需的全部信息（Name + Type）。
/// <para>纯值类型，不持有 native 句柄，session 关闭后仍可安全读取。</para>
/// <remarks>
/// ⚠️ 逃生舱产出——调用方可用 (Name, Type) 自行 P/Invoke
/// <c>ProMdlnameInit</c> 重建 <c>ProMdl</c> 句柄。
/// native 生命周期由调用方负责，SDK 不保证对应模型仍存活。
/// </remarks>
/// </summary>
public readonly record struct NativeModelToken(string Name, CreoModelType Type);

/// <summary>
/// <see cref="CreoModelItem"/> 的 native 身份 DATA 快照。
/// 携带重建 <c>ProModelitem</c> DHandle 所需的全部信息。
/// <para>纯值类型，不持有 native 句柄。</para>
/// <remarks>
/// ⚠️ 逃生舱产出——调用方可用四元组重建 <c>ProModelitem</c>：
/// <c>{ ProType = ItemType, id = Id, owner = ProMdlnameInit(ModelName, ModelType) }</c>。
/// native 生命周期由调用方负责。
/// </remarks>
/// </summary>
public readonly record struct NativeItemToken(
    string ModelName,
    CreoModelType ModelType,
    CreoModelItemType ItemType,
    int Id);

/// <summary>
/// 受控 native 逃生舱：
/// 从 SDK domain 对象提取 native 身份 DATA 快照。
/// <remarks>
/// ⚠️ 使用降级通道意味着脱离常规保护。
/// token 重建后的资源所有权仍由调用方负责；实际 native 调用应通过
/// <see cref="InvokeNative(CreoSession, string, Func{int})"/>，由 session 统一执行
/// 主线程调度、关闭检查和错误分类。
/// SDK 不保证 token 对应的 native 资源在 Creo 运行时仍有效。
/// <para>对标 OTK <c>wfcGetHandleFromObject</c>，v1 仅提供单向 DATA snapshot。
/// 反向 <c>FromNative</c> 待 session 对象表落地后实现。</para>
/// </remarks>
/// </summary>
public static class NativeEscapeHatch
{
    /// <summary>
    /// 经 session dispatcher 执行高级 native 调用，raw Pro/Toolkit 状态码
    /// 按 SDK <see cref="Check"/> 策略统一分类。
    /// </summary>
    /// <remarks>
    /// 回调内可含调用方自有的 P/Invoke 代码，但 borrowed 指针不得逃逸回调生命周期；
    /// native 返回的 owned 资源仍归调用方负责，常规做法应升格为一等 SDK wrapper。
    /// </remarks>
    public static CreoResult InvokeNative(this CreoSession session, string api, Func<int> call)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNullOrWhiteSpace(api);
        ThrowUtil.IfNull(call);

        return session.Run(_ => Check.Eval(api, (ProError)call()));
    }

    /// <summary>
    /// 经 session dispatcher 执行高级 native copy-out 调用，同时返回拷出值与
    /// SDK 分类后的状态。回调不得把 borrowed native 指针作为 <typeparamref name="T"/> 返回。
    /// </summary>
    public static NativeEscapeResult<T> InvokeNative<T>(
        this CreoSession session,
        string api,
        Func<(int Status, T Value)> call)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNullOrWhiteSpace(api);
        ThrowUtil.IfNull(call);

        return session.Run(_ =>
        {
            var (code, value) = call();
            return new NativeEscapeResult<T>(value, Check.Eval(api, (ProError)code));
        });
    }

    /// <summary>
    /// 从 <see cref="CreoModel"/> 提取 native 身份 DATA 快照。
    /// <remarks>
    /// 调用方可用返回的 token 自行 P/Invoke <c>ProMdlnameInit(name, type)</c>
    /// 重建 <c>ProMdl</c> 句柄。native 生命周期由调用方负责。
    /// </remarks>
    /// </summary>
    public static NativeModelToken ToNativeToken(this CreoModel model)
    {
        ThrowUtil.IfNull(model);
        return new NativeModelToken(model.Name, model.Type);
    }

    /// <summary>
    /// 从 <see cref="CreoModelItem"/> 提取 native 身份 DATA 快照。
    /// <para>暴露内部 (ModelName, ModelType, ItemType, Id) 四元组——
    /// 这是主 L3 面不公开的所有权信息。</para>
    /// <remarks>
    /// 调用方可用返回的 token 重建 <c>ProModelitem</c> DHandle：
    /// <c>{ ProType = ItemType, id = Id, owner = ProMdlnameInit(ModelName, ModelType) }</c>。
    /// native 生命周期由调用方负责。
    /// </remarks>
    /// </summary>
    public static NativeItemToken ToNativeToken(this CreoModelItem item)
    {
        ThrowUtil.IfNull(item);
        return new NativeItemToken(
            item.Ref.Model.Name,
            item.Ref.Model.Type,
            item.Type,
            item.Ref.Id);
    }
}
