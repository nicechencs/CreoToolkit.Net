using System.Runtime.InteropServices;
using CreoToolkit.Interop.Diagnostics;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Interop;

/// <summary>
/// L2 识别的唯一释放抽象。默认实现直接 P/Invoke 真实 DLL；L3 在运行时替换为「主线程检查+入队」
/// 释放器；测试注入计数 fake。如此 L2 无需 L1/L3 即可单元测试，也避免 L2→L3 反向依赖。
/// <para><paramref name="kind"/> 选定具体 L1 free（三类互不混用，见 <see cref="CtkFreeKind"/>）——
/// 不同 owned 资源（选择副本集 / LIVE 元素树）使用不同释放函数，由门按 kind 分派。</para>
/// </summary>
public interface ICreoReleaser
{
    /// <summary>释放 <paramref name="handle"/>，按 <paramref name="kind"/> 选对应 L1 free。</summary>
    void Release(nint handle, CtkFreeKind kind);
}

/// <summary>
/// 进程级释放策略注入点，默认为 <see cref="DirectReleaser"/>。
/// L3 或测试代码调用 <see cref="SetReleaser"/> 接管释放路径。
/// </summary>
public static class CreoReleaseGate
{
    private static ICreoReleaser _current = new DirectReleaser(); // 默认直接 P/Invoke
    private static long _directReleaseCount;

    /// <summary>注入释放策略（L3 主线程编排器或测试 fake）。</summary>
    public static void SetReleaser(ICreoReleaser releaser)
        => _current = releaser ?? throw new ArgumentNullException(nameof(releaser));

    /// <summary>恢复默认直接释放器，主要用于测试隔离。</summary>
    public static void ResetToDefault()
    {
        _current = new DirectReleaser();
        Interlocked.Exchange(ref _directReleaseCount, 0);
    }

    /// <summary>诊断计数：经本门路由到默认 <see cref="DirectReleaser"/>（未被 <see cref="SetReleaser"/> 接管）的释放次数。
    /// 生产装配下 <c>Attach</c> 先注入主线程 releaser，此值应恒为 0；非零意味着装配顺序错误（裸直调）。
    /// 测试据此断言"生产路径零直调"。</summary>
    internal static long DirectReleaseCount => Interlocked.Read(ref _directReleaseCount);

    internal static void Release(nint handle, CtkFreeKind kind)
    {
        var current = _current;
        if (current is DirectReleaser)
            Interlocked.Increment(ref _directReleaseCount); // 记录未被接管的裸直调
        current.Release(handle, kind);
    }
}

/// <summary>
/// 默认释放器：将指针直接交给 native free 函数，按 <see cref="CtkFreeKind"/> 分派。
/// 测试场景下在任何句柄析构前注入 fake，此类不被调用。
/// </summary>
internal sealed class DirectReleaser : ICreoReleaser
{
    private static readonly global::Serilog.ILogger Log = CreoSerilog.ForContext("Releaser", CreoLogLayer.L2);

    public void Release(nint handle, CtkFreeKind kind)
    {
        if (handle == IntPtr.Zero)
            return;
        switch (kind)
        {
            case CtkFreeKind.Elemtree:
                var blob = Marshal.PtrToStructure<CtkElemtreeBlob>(handle);
                var feat = blob.Feat;
                int rc;
                try
                {
                    rc = (int)G.ProFeatureElemtreeFree(ref feat, blob.Tree);
                }
                finally
                {
                    Marshal.FreeHGlobal(handle);
                }

                if (rc != 0)
                    CreoSerilog.Error(
                        Log,
                        "ProFeatureElemtreeFree failed rc={Rc} tree=0x{Tree:x}",
                        propertyValues: new object?[] { rc, blob.Tree },
                        @event: "resource.release.fail",
                        props: new { kind = nameof(CtkFreeKind.Elemtree), tree = blob.Tree.ToString("x"), rc });
                else
                    CreoSerilog.Verbose(
                        Log,
                        "ProFeatureElemtreeFree ok tree=0x{Tree:x}",
                        propertyValues: new object?[] { blob.Tree },
                        @event: "resource.release.ok",
                        props: new { kind = nameof(CtkFreeKind.Elemtree), tree = blob.Tree.ToString("x"), rc });
                break;
            case CtkFreeKind.Selection:
                var sel = handle;                       // 句柄即 owned ProSelection 副本指针
                int selRc = (int)G.ProSelectionFree(ref sel);
                if (selRc != 0)
                    CreoSerilog.Error(
                        Log,
                        "ProSelectionFree failed rc={Rc} sel=0x{Sel:x}",
                        propertyValues: new object?[] { selRc, handle },
                        @event: "resource.release.fail",
                        props: new { kind = nameof(CtkFreeKind.Selection), sel = handle.ToString("x"), rc = selRc });
                else
                    CreoSerilog.Verbose(
                        Log,
                        "ProSelectionFree ok sel=0x{Sel:x}",
                        propertyValues: new object?[] { handle },
                        @event: "resource.release.ok",
                        props: new { kind = nameof(CtkFreeKind.Selection), sel = handle.ToString("x"), rc = selRc });
                break;
            default:
                NativeMethods.CreoToolkit_Free(handle);  // CtkOwnedBlock：按 kind 内部分派
                CreoSerilog.Verbose(
                    Log,
                    "CreoToolkit_Free ok handle=0x{Handle:x}",
                    propertyValues: new object?[] { handle },
                    @event: "resource.release.ok",
                    props: new { kind = kind.ToString(), handle = handle.ToString("x") });
                break;
        }
    }
}
