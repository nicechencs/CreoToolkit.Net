using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    /// <summary>取坐标系 4×4 变换矩阵(ProCsysInit + ProCsysDataGet + CreoTransform.MatrixFromAxes)。
    /// <para>ProCsysDataGet 返 BORROWED static <c>csys_data_struct*</c>(4 个 double[3] = x/y/z/origin);
    /// 不可 free。直接读 12 个 double 拼成行优先 4×4 矩阵。owner 不在会话或 csys id 不存在 → null。</para>
    /// <para>3 条 null 早退 emit <c>csys.matrixget.{reason}</c> trace
    /// (reason ∈ owner_unresolved / init_notfound / dataget_notfound)。</para></summary>
    public double[]? CsysMatrixGet(ItemRef csys)
    {
        if (!TryResolveMdl(csys, out var mdl))
        {
            CreoSdkLog.Trace("csys", "matrixget.owner_unresolved",
                new { model = csys.Model.Name, id = csys.Id });
            return null;
        }

        var initRc = G.ProCsysInit(mdl, csys.Id, out var handle);
        if (IsNotFound(initRc) || handle == IntPtr.Zero)
        {
            CreoSdkLog.Trace("csys", "matrixget.init_notfound",
                new { model = csys.Model.Name, id = csys.Id });
            return null;
        }
        Check.Eval(nameof(G.ProCsysInit), initRc);

        var rc = G.ProCsysDataGet(handle, out IntPtr dataPtr);
        if (IsNotFound(rc) || dataPtr == IntPtr.Zero)
        {
            CreoSdkLog.Trace("csys", "matrixget.dataget_notfound",
                new { model = csys.Model.Name, id = csys.Id });
            return null;
        }
        Check.Eval(nameof(G.ProCsysDataGet), rc);

        // ICreoNative 边界保留 double[16] (L2 ABI 不变);直接读 12 个 double 拼行优先 4×4
        var arr = new double[16];
        unsafe
        {
            var p = (double*)dataPtr;
            // 前 3 行 = x/y/z 轴, 第 4 列齐次 0
            arr[0]  = p[0];  arr[1]  = p[1];  arr[2]  = p[2];  arr[3]  = 0.0;
            arr[4]  = p[3];  arr[5]  = p[4];  arr[6]  = p[5];  arr[7]  = 0.0;
            arr[8]  = p[6];  arr[9]  = p[7];  arr[10] = p[8];  arr[11] = 0.0;
            // 第 4 行 = origin, 第 4 列齐次 1
            arr[12] = p[9];  arr[13] = p[10]; arr[14] = p[11]; arr[15] = 1.0;
        }
        return arr;
    }
}
