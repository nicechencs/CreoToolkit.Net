using System.Runtime.InteropServices;

namespace CreoToolkit.Interop;

/// <summary>
/// 启动期 ABI 自校验: 调 <c>Ctk_GetAbiInfo</c> 取 native 真值, 与托管镜像 <see cref="Marshal.SizeOf{T}()"/> /
/// <see cref="Marshal.OffsetOf(System.Type, string)"/> + <see cref="CtkAbiConstants"/> 逐项断言。任何不符即抛
/// (fail-fast), 杜绝 <see cref="StructLayoutAttribute"/> 与 native 头偷偷错位。
/// <para>需真实 DLL 在场(Creo 进程内, NativeHost 已 LoadLibrary 且 CLR 4 已启动后), 故仅运行期/真实 e2e 调用; 脱-Creo 的纯托管布局
/// 正确性由 Interop 单测覆盖。</para>
/// </summary>
public static class CtkAbi
{
    /// <summary>取 native ABI 信息并断言与托管镜像一致; 不符抛 <see cref="InvalidOperationException"/>。</summary>
    public static void Verify()
    {
        NativeMethods.Ctk_GetAbiInfo(out var info);

        Check(info.AbiInfoSize, Marshal.SizeOf<CtkAbiInfo>(), "sizeof(CtkAbiInfo)");
        Check(info.WcharSize, 2, "sizeof(wchar_t)");
        Check(info.ProNameSize, CtkAbiConstants.ProNameSize, "PRO_NAME_SIZE");
        Check(info.ProLineSize, CtkAbiConstants.ProLineSize, "PRO_LINE_SIZE");
        Check(info.ParamRecordSize, Marshal.SizeOf<CtkParamRecord>(), "sizeof(CtkParamRecord)");
        Check(info.OffName, OffsetOf(nameof(CtkParamRecord.Name)), "offsetof(CtkParamRecord, name)");
        Check(info.OffKind, OffsetOf(nameof(CtkParamRecord.Kind)), "offsetof(CtkParamRecord, kind)");
        Check(info.OffIVal, OffsetOf(nameof(CtkParamRecord.IVal)), "offsetof(CtkParamRecord, i_val)");
        Check(info.OffDVal, OffsetOf(nameof(CtkParamRecord.DVal)), "offsetof(CtkParamRecord, d_val)");
        Check(info.OffSVal, OffsetOf(nameof(CtkParamRecord.SVal)), "offsetof(CtkParamRecord, s_val)");
        Check(info.OffModified, OffsetOf(nameof(CtkParamRecord.Modified)), "offsetof(CtkParamRecord, modified)");
        Check(info.ElemNodeSize, Marshal.SizeOf<CtkElemNode>(), "sizeof(CtkElemNode)");
        Check(info.NameRecordSize, Marshal.SizeOf<CtkNameRecord>(), "sizeof(CtkNameRecord)");
    }

    private static int OffsetOf(string field) => (int)Marshal.OffsetOf<CtkParamRecord>(field);

    private static void Check(int native, int managed, string what)
    {
        if (native != managed)
            throw new InvalidOperationException(
                $"L1 ABI 不匹配: {what} native={native} 托管={managed}。镜像结构体与 native 头不一致, 拒绝继续。");
    }
}
