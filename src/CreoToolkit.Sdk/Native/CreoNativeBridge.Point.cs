using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    /// <summary>取基准点 3D 坐标(ProPointInit + ProPointCoordGet, DATA copy-out)。
    /// owner 不在会话或点 id 不存在 → null。ProPointInit 返句柄为 BORROWED static, 无需 free。
    /// <para>3 条 null 早退 emit <c>point.coordget.{reason}</c> trace
    /// (reason ∈ owner_unresolved / init_notfound / coordget_notfound)。</para></summary>
    public double[]? PointCoordGet(ItemRef point)
    {
        if (!TryResolveMdl(point, out var mdl))
        {
            CreoSdkLog.Trace("point", "coordget.owner_unresolved",
                new { model = point.Model.Name, id = point.Id });
            return null;
        }

        var initRc = G.ProPointInit(mdl, point.Id, out var handle);
        if (IsNotFound(initRc) || handle == IntPtr.Zero)
        {
            CreoSdkLog.Trace("point", "coordget.init_notfound",
                new { model = point.Model.Name, id = point.Id });
            return null;
        }
        Check.Eval(nameof(G.ProPointInit), initRc);

        var xyz = new double[3];
        var rc = G.ProPointCoordGet(handle, xyz);
        if (IsNotFound(rc))
        {
            CreoSdkLog.Trace("point", "coordget.coordget_notfound",
                new { model = point.Model.Name, id = point.Id });
            return null;
        }
        Check.Eval(nameof(G.ProPointCoordGet), rc);
        return xyz;
    }
}
