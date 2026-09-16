using CreoToolkit.Sdk.Diagnostics;

namespace CreoToolkit.Sdk;

/// <summary><see cref="CreoDtlentity"/> 守门 helper。
/// <para>抛 <see cref="InvalidOperationException"/> 对齐 <see cref="CreoUnitGuard"/> 现纪律。</para>
/// <para>抛前 warn trace 出 reason,event=<c>drawing.dtlentity-guard.{uninitialized|epoch_mismatch}</c>,
/// 排查 InvalidOperationException 时可直接日志聚类定位。</para></summary>
internal static class CreoDtlentityGuard
{
    /// <summary>default(CreoDtlentity) 入参拒。
    /// <para>用 <c>default</c> 值意味着调用方手搓而非经 <see cref="CreoDrawing.ListDtlentities"/> 工厂取得。</para></summary>
    public static void NotUninitialized(CreoDtlentity entity, string paramName)
    {
        if (entity.IsUninitialized)
        {
            CreoSdkLog.Warn(
                "drawing", "dtlentity-guard.uninitialized",
                "default(CreoDtlentity) 入参拒(只能经 CreoDrawing.ListDtlentities 工厂取得)",
                new { paramName });
            throw new InvalidOperationException(
                $"CreoDtlentity '{paramName}' 是 default 值,只能经 CreoDrawing.ListDtlentities 工厂取得。");
        }
    }

    /// <summary>校验 entity 携带的 epoch 与当前 session epoch 一致 (long, 防序列回绕)。
    /// <para>不一致 → 抛 <see cref="InvalidOperationException"/>(异 session 误传 / 当前 session 已 dispose 后重 Attach 等)。</para></summary>
    public static void SameSessionEpoch(CreoDtlentity entity, long currentEpoch, string paramName)
    {
        if (entity.SessionEpoch != currentEpoch)
        {
            CreoSdkLog.Warn(
                "drawing", "dtlentity-guard.epoch_mismatch",
                "CreoDtlentity 来自异 session(epoch 不匹配,跨 session 误传或 Dispose 后重 Attach)",
                new { paramName, entityEpoch = entity.SessionEpoch, currentEpoch });
            throw new InvalidOperationException(
                $"CreoDtlentity '{paramName}' 来自 epoch={entity.SessionEpoch} 的 CreoSession,当前 session epoch={currentEpoch} — 不能跨 session 使用。");
        }
    }
}
