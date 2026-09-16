using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 坐标系(OHandle 衍生;session-bound)。操作留 future 按业务排队加。
/// </summary>
public sealed class CreoCsys : CreoModelItem
{
    internal CreoCsys(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>坐标系 4×4 变换矩阵(直绑 <c>ProCsysInit + ProCsysDataGet</c>, DATA)。
    /// owner 不在会话或 csys id 不存在 → null。返 <see cref="CreoMat4"/> 行优先 —
    /// 可直接经 <see cref="CreoMat4.TransformPoint"/> 把局部点变到 owner 模型坐标系。</summary>
    public CreoMat4? GetMatrix()
    {
        var arr = Session.Run(n => n.CsysMatrixGet(Ref));
        CreoSdkLog.Trace("csys", "getmatrix",
            new { model = Ref.Model.Name, id = Ref.Id, found = arr is not null });
        return arr is null ? null : CreoMat4.FromArray(arr);
    }

    /// <summary>坐标系几何视图(Origin + X/Y/Z 轴,纯 .NET 派生自 <see cref="GetMatrix"/>)。
    /// owner 不在会话或 csys id 不存在 → null。等价 <c>GetMatrix().Value.</c>抽前 3 行轴 + 第 4 行原点。
    /// 单次 native 调用;比逐字段 <c>GetXAxis/Y/Z/Origin</c> 4 次往返高效,推荐"四字段都要"时首选。</summary>
    public CreoCoordSystem? GetCoordSystem()
    {
        var m = GetMatrix();
        // 注:不再额外 trace — GetMatrix 内部已 emit csys.getmatrix,
        // GetCoordSystem 是纯 .NET 派生(0 next-layer call),再 trace 一遍是 GetMatrix 镜像噪音。
        return m is null ? null : CreoCoordSystem.FromMatrix(m.Value);
    }
}
