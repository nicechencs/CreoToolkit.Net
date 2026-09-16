using CreoToolkit.Interop.Generated;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    /// <summary>取 PLANE 类型面的几何数据。
    /// <para>调 <c>ProSurfaceInit</c>(BORROWED handle)+ <c>ProSurfaceTypeGet</c>(校验 PLANE)
    /// + <c>ProSurfaceXyzdataEval(uv=(0,0))</c> 在 UV 点直接求 (xyz, deriv1[2], normal);
    /// 对 PLANE 任意 UV 给同一 normal(平面恒法向),xyz/du/dv 即面上一点 + 两个切向,
    /// 数学上 (p − Origin) · Normal = 0 成立 — 满足下游 <see cref="CreoPlane"/> 距离/投影计算。</para>
    /// <para>⚠️ <b>历史 trap</b>(真 Creo 实证):
    /// 原实现先用 <c>ProSurfacedataGet(ref ptc_surf, ..., (IntPtr)&amp;shape, ...)</c>
    /// + <c>ProPlanedataGet(&amp;shape, ...)</c>,然后 finally 调
    /// <c>ProSurfacedataMemoryFree(ref surf)</c>。两个根本性错误:
    /// <list type="number">
    /// <item><c>ProSurfacedataGet</c>(d 小写)是从**已初始化的** <c>ProSurfacedata</c> 结构里**拆**
    ///   数据,**不是**从 surface handle 取数据(那是 <c>ProSurfaceDataGet</c>,D 大写)。
    ///   传 <c>default(ptc_surf)</c> 空结构进去,Get 拷贝空 shape 到 <c>&amp;shape</c>,
    ///   PlanedataGet 拿到全零 plane → e3=(0,0,0) → CreoPlane.SignedDistanceTo 抛
    ///   InvalidOperationException("Plane normal is zero")。</item>
    /// <item><c>ProSurfacedataMemoryFree(ref ptc_surf)</c> 在 non-blittable struct
    ///   marshalling box-copy 下,对 default-init 的 surf 内部 IntPtr 字段 deref → AV →
    ///   Creo 整进程死(Windows EventLog `.NET Runtime` id=1026)。</item>
    /// </list>
    /// 改 <c>ProSurfaceXyzdataEval</c>:输入只有 BORROWED handle + UV 数组,输出全是
    /// <c>double[]</c> marshalling(ProVector = double[3]),**完全避开 ptc_surf non-blittable
    /// 结构 + 无 alloc/free 配对**,根除上述两个 trap。代价:Origin 不再是建模时存的"plane origin"
    /// 而是 UV(0,0) 处面上的一点,对距离/投影语义无影响(任意点都可作 plane origin)。
    /// 拿其他面型(splsrf/cylinder 等)请用 <c>ProSurfaceDataGet</c>(D 大写)+
    /// <c>ProGeomitemdata</c> 路径,marshalling 风险另议。</para>
    /// <para>每条 null 早退路径都 emit <c>surface.planedataget.{reason}</c> trace
    /// (reason ∈ owner_unresolved / init_notfound / type_failed / not_plane / eval_failed),
    /// 开发期定位返 null 原因(production warn 阈值自动屏蔽)。</para></summary>
    public SurfacePlaneData? SurfacePlaneDataGet(ItemRef surface)
    {
        if (!TryResolveMdl(surface, out var mdl))
        {
            CreoSdkLog.Trace("surface", "planedataget.owner_unresolved",
                new { model = surface.Model.Name, id = surface.Id });
            return null;
        }

        // 1) ProSurfaceInit 返 BORROWED handle(库 static,不 free)
        int rcInit = (int)G.ProSurfaceInit(mdl, surface.Id, out var pSurf);
        if (rcInit < 0 || pSurf == IntPtr.Zero)
        {
            CreoSdkLog.Trace("surface", "planedataget.init_notfound",
                new { model = surface.Model.Name, id = surface.Id, rc = rcInit });
            return null;
        }

        // 2) 早退:非 PLANE 类型不走 plane 数据抽取
        int rcType = (int)G.ProSurfaceTypeGet(pSurf, out var srfType);
        if (rcType < 0)
        {
            CreoSdkLog.Trace("surface", "planedataget.type_failed",
                new { model = surface.Model.Name, id = surface.Id, rc = rcType });
            return null;
        }
        Ensure(nameof(G.ProSurfaceTypeGet), rcType);
        if (srfType != pro_srf_type.PRO_SRF_PLANE)
        {
            CreoSdkLog.Trace("surface", "planedataget.not_plane",
                new { model = surface.Model.Name, id = surface.Id, srfType = (int)srfType });
            return null;
        }

        // 3) ProSurfaceXyzdataEval 在 UV=(0,0) 直接求 (xyz, deriv1, normal)
        //    deriv1 是 ProVector[2] = du/dv 两个切向(对 PLANE 即面内基 e1/e2)
        //    deriv2 是 ProVector[3] = 二阶导,本调用未用但 PTC 签名必传
        var uv = new double[2] { 0.0, 0.0 };
        var xyz = new double[3];
        var deriv1 = new double[6];
        var deriv2 = new double[9];
        var normal = new double[3];
        int rcEval = (int)G.ProSurfaceXyzdataEval(pSurf, uv, xyz, deriv1, deriv2, normal);
        if (rcEval < 0)
        {
            CreoSdkLog.Trace("surface", "planedataget.eval_failed",
                new { model = surface.Model.Name, id = surface.Id, rc = rcEval });
            return null;
        }
        Ensure(nameof(G.ProSurfaceXyzdataEval), rcEval);

        // 4) 拼装:Origin=xyz,E1=deriv1[0..3](du),E2=deriv1[3..6](dv),E3=normal(PTC 已归一)
        var e1 = new double[3] { deriv1[0], deriv1[1], deriv1[2] };
        var e2 = new double[3] { deriv1[3], deriv1[4], deriv1[5] };
        return new SurfacePlaneData(E1: e1, E2: e2, E3: normal, Origin: xyz);
    }
}
