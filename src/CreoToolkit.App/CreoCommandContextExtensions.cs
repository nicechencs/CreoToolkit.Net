using System.Diagnostics.CodeAnalysis;
using CreoToolkit.Sdk;

namespace CreoToolkit.App;

/// <summary>
/// <see cref="CreoCommandContext"/> 扩展助手：减少 11 Module ~215 行重复 boilerplate。
/// 设计原则：不引入新抽象层，仅以 `[NotNullWhen(true)]` out 模式收敛 "guard or message + early return"。
/// A+ 路线（避免 SDK 污染 + Samples.Shared 过度抽象）。
/// </summary>
public static class CreoCommandContextExtensions
{
    /// <summary>统一 "无 Creo session" 用户反馈文案(Module 复用避免字面量散落 42 次)。</summary>
    public const string NoActiveSession = "No active Creo session";

    /// <summary>统一 "无当前模型" 用户反馈文案。</summary>
    public const string NoCurrentModel = "No current model";

    /// <summary>
    /// 校验有活跃 Session,缺失时报 <see cref="NoActiveSession"/> 并返回 false(Module 应 early return)。
    /// 用法: <c>if (!ctx.RequireSession(out var session)) return;</c>
    /// </summary>
    public static bool RequireSession(
        this CreoCommandContext ctx,
        [NotNullWhen(true)] out CreoSession? session)
    {
        ThrowUtil.IfNull(ctx);
        if (ctx.Session is null)
        {
            ctx.Messages.Info(NoActiveSession);
            session = null;
            return false;
        }
        session = ctx.Session;
        return true;
    }

    /// <summary>
    /// 校验有活跃 Session + 有 current 模型,任一缺失即报对应消息并返回 false。
    /// 用法: <c>if (!ctx.RequireCurrentModel(out var session, out var model)) return;</c>
    /// </summary>
    public static bool RequireCurrentModel(
        this CreoCommandContext ctx,
        [NotNullWhen(true)] out CreoSession? session,
        [NotNullWhen(true)] out CreoModel? model)
    {
        if (!ctx.RequireSession(out session))
        {
            model = null;
            return false;
        }
        model = session.Models.GetCurrent();
        if (model is null)
        {
            ctx.Messages.Info(NoCurrentModel);
            return false;
        }
        return true;
    }

    /// <summary>
    /// 校验 Session + 按名称/类型检索模型,失败(无 Session 或 Retrieve 抛异常)报对应消息返回 false。
    /// 用法: <c>if (!ctx.TryRetrieveModel("exercise4", CreoModelType.Part, out var model)) return;</c>
    /// </summary>
    public static bool TryRetrieveModel(
        this CreoCommandContext ctx,
        string name,
        CreoModelType type,
        [NotNullWhen(true)] out CreoModel? model)
    {
        if (!ctx.RequireSession(out var session))
        {
            model = null;
            return false;
        }
        try
        {
            model = session.Models.Retrieve(name, type);
            return model is not null;
        }
        catch (Exception ex)
        {
            ctx.Messages.Info($"Retrieve '{name}' failed: {ex.Message}");
            model = null;
            return false;
        }
    }
}
