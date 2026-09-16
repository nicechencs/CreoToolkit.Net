using System.Diagnostics;
using CreoToolkit.Interop.Diagnostics;

namespace CreoToolkit.Sdk.Diagnostics;

/// <summary>
/// SDK 层结构化遥测门面(telemetry-only)。
/// 调用方传 domain + action + props,helper 起 Activity 名为 "domain.action",
/// 经 <see cref="CreoTelemetry.SdkSource"/> 暴露给 ActivityListener / OpenTelemetry exporter。
///
/// 用法:
///   CreoSdkLog.Ok("layer", "create", new { name, created });
///   CreoSdkLog.Fail("parameter", "set", new { model = m.Name, name, value }, ex);
///
/// 命名约定:domain 用名词单数(model/layer/feature/parameter/surface/drawing 等),
/// action 全小写(load/save/create/delete/set 等)。
///
/// 说明:SDK 层不再直接写日志(JSONL / sink)。日志聚合由 Host/App/Logging
/// 通过 ActivityListener 订阅本 ActivitySource,再决定是否落盘 / 转发。
/// </summary>
internal static class CreoSdkLog
{
    /// <summary>void op helper:helper 拥有 StartActivity + ctk.domain/action tag + try/catch + Ok/Fail + ex.Data。</summary>
    public static void Run(
        string domain,
        string action,
        Action body,
        Func<object>? failProps = null,
        Func<object>? okProps = null)
    {
        using var activity = StartOp(domain, action);

        try
        {
            body();
            Ok(domain, action, okProps?.Invoke() ?? failProps?.Invoke());
        }
        catch (Exception ex)
        {
            AttachOperationData(ex, domain, action);
            Fail(domain, action, failProps?.Invoke(), ex);
            throw;
        }
    }

    /// <summary>非 void op helper:同上 + 返回业务 body 结果。okProps 接 result 参数,可推出 result-derived props。</summary>
    public static T Run<T>(
        string domain,
        string action,
        Func<T> body,
        Func<object>? failProps = null,
        Func<T, object>? okProps = null)
    {
        using var activity = StartOp(domain, action);

        try
        {
            var result = body();
            Ok(domain, action, okProps?.Invoke(result) ?? failProps?.Invoke());
            return result;
        }
        catch (Exception ex)
        {
            AttachOperationData(ex, domain, action);
            Fail(domain, action, failProps?.Invoke(), ex);
            throw;
        }
    }

    /// <summary>Loader/Probe 类 op 的 Helper:支持第三路径 Warn(soft miss / 非异常失败)。
    /// 普通动作型 op 用 Run&lt;T&gt;,Loader 类(model.load / find / probe / open / resolve 等)用本重载。</summary>
    public static T RunWithWarn<T>(
        string domain,
        string action,
        Func<T> body,
        Func<object>? failProps = null,
        Func<T, object>? okProps = null,
        Func<T, WarnSpec?>? warnProbe = null)
    {
        using var activity = StartOp(domain, action);

        try
        {
            var result = body();
            var warn = warnProbe?.Invoke(result);
            if (warn is { } spec)
                Warn(domain, spec.Action, spec.Detail, spec.Props);
            else
                Ok(domain, action, okProps?.Invoke(result) ?? failProps?.Invoke());
            return result;
        }
        catch (Exception ex)
        {
            AttachOperationData(ex, domain, action);
            Fail(domain, action, failProps?.Invoke(), ex);
            throw;
        }
    }

    /// <summary>Quiet 变体:catch 后吞异常不 rethrow(用于 cleanup / dispose 链路)。</summary>
    public static void RunQuiet(
        string domain,
        string action,
        Action body,
        Func<object>? failProps = null,
        Func<object>? okProps = null)
    {
        using var activity = StartOp(domain, action);

        try
        {
            body();
            Ok(domain, action, okProps?.Invoke() ?? failProps?.Invoke());
        }
        catch (Exception ex)
        {
            AttachOperationData(ex, domain, action);
            Fail(domain, action, failProps?.Invoke(), ex);
        }
    }

    /// <summary>Quiet 非 void 变体:catch 后吞异常返 default(T)。</summary>
    public static T? RunQuiet<T>(
        string domain,
        string action,
        Func<T> body,
        Func<object>? failProps = null,
        Func<T, object>? okProps = null)
    {
        using var activity = StartOp(domain, action);

        try
        {
            var result = body();
            Ok(domain, action, okProps?.Invoke(result) ?? failProps?.Invoke());
            return result;
        }
        catch (Exception ex)
        {
            AttachOperationData(ex, domain, action);
            Fail(domain, action, failProps?.Invoke(), ex);
            return default;
        }
    }

    /// <summary>成功路径:把 ok props 写入当前 Activity 并设状态 Ok。无 Activity 时静默(Run* 系列保证有 Activity)。</summary>
    public static void Ok(string domain, string action, object? props = null)
    {
        var activity = Activity.Current;
        if (activity == null)
            return;

        activity.SetStatus(ActivityStatusCode.Ok);
        AddPropsAsTags(activity, props);
    }

    /// <summary>失败路径:把 fail props + 异常信息写入当前 Activity 并设状态 Error。无 Activity 时静默。</summary>
    public static void Fail(string domain, string action, object? props, Exception ex)
    {
        var activity = Activity.Current;
        if (activity == null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.SetTag("exception.type", ex.GetType().FullName);
        activity.SetTag("exception.message", ex.Message);
        AddPropsAsTags(activity, props);
    }

    /// <summary>Warn(soft miss / 非异常失败)telemetry 化:
    /// 当前有 Activity → 以 Activity event 形式追加 "{domain}.{action}",并把 detail / props 入 event tag;
    /// 无 Activity → 起一个短 Activity,listener 仍可观察。</summary>
    public static void Warn(string domain, string action, string detail, object? props = null)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            var eventTags = BuildEventTags(domain, action, props);
            eventTags["ctk.detail"] = detail;
            activity.AddEvent(new ActivityEvent($"{domain}.{action}", default, eventTags));
            AddPropsAsTags(activity, props);
            return;
        }

        using var local = CreoTelemetry.SdkSource.StartActivity($"{domain}.{action}");
        if (local == null)
            return;
        local.SetTag("ctk.domain", domain);
        local.SetTag("ctk.action", action);
        local.SetTag("ctk.detail", detail);
        AddPropsAsTags(local, props);
    }

    /// <summary>读路径高频 trace(Find/List/Description 等)telemetry 化:
    /// 当前有 Activity → 追加一个 Activity event,不污染父 Activity 状态;
    /// 无 Activity → 静默跳过,避免把高频读路径升级为独立 telemetry span。
    /// event 语义:"{domain}.{action}",不带 ok/fail 后缀。</summary>
    public static void Trace(string domain, string action, object? props = null)
    {
        var activity = Activity.Current;
        if (activity == null)
            return;

        var eventTags = BuildEventTags(domain, action, props);
        activity.AddEvent(new ActivityEvent($"{domain}.{action}", default, eventTags));
    }

    private static Activity? StartOp(string domain, string action)
    {
        var activity = CreoTelemetry.SdkSource.StartActivity($"{domain}.{action}");
        activity?.SetTag("ctk.domain", domain);
        activity?.SetTag("ctk.action", action);
        return activity;
    }

    private static void AttachOperationData(Exception ex, string domain, string action)
    {
        ex.Data["ctk.op"] = $"{domain}.{action}";
        ex.Data["ctk.domain"] = domain;
        ex.Data["ctk.action"] = action;
    }

    private static void AddPropsAsTags(Activity activity, object? props)
    {
        if (props == null)
            return;

        try
        {
            foreach (var prop in props.GetType().GetProperties())
            {
                try
                {
                    activity.SetTag($"ctk.{prop.Name}", prop.GetValue(props)?.ToString());
                }
                catch
                {
                    // props 只是辅助诊断信息,反射失败不能影响业务路径。
                }
            }
        }
        catch
        {
            // props 只是辅助诊断信息,反射失败不能影响业务路径。
        }
    }

    private static ActivityTagsCollection BuildEventTags(string domain, string action, object? props)
    {
        var tags = new ActivityTagsCollection
        {
            ["ctk.domain"] = domain,
            ["ctk.action"] = action,
        };

        if (props == null)
            return tags;

        try
        {
            foreach (var prop in props.GetType().GetProperties())
            {
                try
                {
                    tags[$"ctk.{prop.Name}"] = prop.GetValue(props)?.ToString();
                }
                catch
                {
                    // props 只是辅助诊断信息,反射失败不能影响业务路径。
                }
            }
        }
        catch
        {
            // props 只是辅助诊断信息,反射失败不能影响业务路径。
        }

        return tags;
    }
}

/// <summary>RunWithWarn&lt;T&gt; 的 warn 路径返回值。null 表示无 warn。</summary>
public readonly record struct WarnSpec(string Action, string Detail, object? Props);
