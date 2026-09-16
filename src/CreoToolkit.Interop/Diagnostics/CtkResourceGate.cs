using System.Text;

namespace CreoToolkit.Interop.Diagnostics;

/// <summary>
/// 生产程序集级资源收支计数器。按 <see cref="CtkFreeKind"/> 维度记录 SafeHandle 的
/// alloc/free 配对，供测试断言"生产代码路径的资源收支平衡"。
/// <para>
/// 纯 <see cref="System.Threading.Interlocked"/> 计数，零锁零分配零日志；
/// 生产常态运行开销仅两条原子递增（alloc + free 各一条）。
/// <see cref="Report"/> 仅在显式调用时构造字符串。
/// </para>
/// <para>
/// 与 native 侧的计数独立互不耦合——
/// 两套计数可并行断言，各证各的路径。
/// </para>
/// <para>
/// DATA 维度（<c>Marshal.AllocHGlobal</c> / <c>FreeHGlobal</c>）故意不计数：
/// 这些分配是作用域局部 try/finally 配对，无统一收口，半覆盖比不覆盖更糟。
/// </para>
/// </summary>
public static class CtkResourceGate
{
    // CtkFreeKind: OwnedBlock=0, Elemtree=1, Selection=2
    private const int KindCount = 3;

    private static readonly long[] _alloc = new long[KindCount];
    private static readonly long[] _free = new long[KindCount];

    /// <summary>
    /// 记录一次 owned 资源接管（仅 <see cref="CreoSafeHandle.Assign"/> 内部调用）。
    /// </summary>
    internal static void RecordAlloc(CtkFreeKind kind)
    {
        int i = (int)kind;
        if ((uint)i < KindCount)
            Interlocked.Increment(ref _alloc[i]);
    }

    /// <summary>
    /// 记录一次 owned 资源释放（仅 <see cref="CreoSafeHandle.ReleaseHandle"/> 内部调用）。
    /// </summary>
    internal static void RecordFree(CtkFreeKind kind)
    {
        int i = (int)kind;
        if ((uint)i < KindCount)
            Interlocked.Increment(ref _free[i]);
    }

    /// <summary>清零全部计数，测试隔离用。</summary>
    public static void Reset()
    {
        for (int i = 0; i < KindCount; i++)
        {
            Interlocked.Exchange(ref _alloc[i], 0);
            Interlocked.Exchange(ref _free[i], 0);
        }
    }

    /// <summary>
    /// 所有维度是否 alloc == free（即全部资源已平衡释放）。
    /// </summary>
    public static bool IsAllBalanced
    {
        get
        {
            for (int i = 0; i < KindCount; i++)
            {
                if (Interlocked.Read(ref _alloc[i]) != Interlocked.Read(ref _free[i]))
                    return false;
            }
            return true;
        }
    }

    /// <summary>指定维度的未释放数（alloc - free）。</summary>
    public static long Outstanding(CtkFreeKind kind)
    {
        int i = (int)kind;
        if ((uint)i >= KindCount) return 0;
        return Interlocked.Read(ref _alloc[i]) - Interlocked.Read(ref _free[i]);
    }

    /// <summary>指定维度的累计分配数。</summary>
    public static long TotalAlloc(CtkFreeKind kind)
    {
        int i = (int)kind;
        return (uint)i < KindCount ? Interlocked.Read(ref _alloc[i]) : 0;
    }

    /// <summary>指定维度的累计释放数。</summary>
    public static long TotalFree(CtkFreeKind kind)
    {
        int i = (int)kind;
        return (uint)i < KindCount ? Interlocked.Read(ref _free[i]) : 0;
    }

    /// <summary>
    /// 人可读的资源收支报告（仅显式调用时构造字符串）。
    /// 格式与 native <c>gate_res_report</c> 对齐。
    /// </summary>
    public static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---- managed resource accounting ----");
        var kinds = (CtkFreeKind[])Enum.GetValues(typeof(CtkFreeKind));
        foreach (var kind in kinds)
        {
            int i = (int)kind;
            if ((uint)i >= KindCount) continue;
            long a = Interlocked.Read(ref _alloc[i]);
            long f = Interlocked.Read(ref _free[i]);
            long leaked = a - f;
            sb.AppendFormat("  {0,-16} allocated={1} freed={2} leaked={3}{4}",
                kind, a, f, leaked, leaked == 0 ? "" : "  <<< LEAK");
            sb.AppendLine();
        }
        bool balanced = IsAllBalanced;
        sb.AppendFormat("  overall: {0}", balanced ? "BALANCED (leaked=0)" : "UNBALANCED (LEAK)");
        sb.AppendLine();
        return sb.ToString();
    }
}
