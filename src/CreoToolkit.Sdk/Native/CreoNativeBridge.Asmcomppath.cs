using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    private const int ProCompPathMax = 25;

    public ModelIdentity? AssemblyPathMdlGet(ModelIdentity assembly, IReadOnlyList<int> compIdPath)
    {
        if (!TryInitAsmcompPath(assembly, compIdPath, out var path))
        {
            CreoSdkLog.Trace("assembly", "pathmdlget.path_init_failed",
                new { assembly = assembly.Name, pathLen = compIdPath?.Count ?? 0 });
            return null;
        }

        var rc = G.ProAsmcomppathMdlGet(ref path, out var child);
        if (IsAsmcompPathMiss(rc) || child == IntPtr.Zero)
        {
            CreoSdkLog.Trace("assembly", "pathmdlget.miss",
                new { assembly = assembly.Name, pathLen = compIdPath.Count });
            return null;
        }
        Check.Eval(nameof(G.ProAsmcomppathMdlGet), rc);

        var nameBuf = new char[180];
        Check.Eval(nameof(G.ProMdlMdlnameGet), G.ProMdlMdlnameGet(child, nameBuf));
        Check.Eval(nameof(G.ProMdlTypeGet), G.ProMdlTypeGet(child, out var type));
        return new ModelIdentity(FromProName(nameBuf), FromProMdlType((int)type));
    }

    public double[]? AssemblyPathTransformGet(
        ModelIdentity assembly,
        IReadOnlyList<int> compIdPath,
        bool localToTop)
    {
        if (!TryInitAsmcompPath(assembly, compIdPath, out var path))
        {
            CreoSdkLog.Trace("assembly", "pathtransformget.path_init_failed",
                new { assembly = assembly.Name, pathLen = compIdPath?.Count ?? 0, localToTop });
            return null;
        }

        var matrix = new double[16];
        var bottomUp = localToTop ? GNS.ProBooleans.PRO_B_TRUE : GNS.ProBooleans.PRO_B_FALSE;
        var rc = G.ProAsmcomppathTrfGet(ref path, bottomUp, matrix);
        if (IsAsmcompPathMiss(rc))
        {
            CreoSdkLog.Trace("assembly", "pathtransformget.miss",
                new { assembly = assembly.Name, pathLen = compIdPath.Count, localToTop });
            return null;
        }
        Check.Eval(nameof(G.ProAsmcomppathTrfGet), rc);
        return matrix;
    }

    private static bool TryInitAsmcompPath(
        ModelIdentity assembly,
        IReadOnlyList<int> compIdPath,
        out GNS.pro_comp_path path)
    {
        ThrowUtil.IfNull(compIdPath);
        if (assembly.Type != CreoModelType.Assembly)
            throw new InvalidOperationException("Assembly component paths require an assembly model.");
        if (compIdPath.Count > ProCompPathMax)
            throw new ArgumentException($"Component path length must be <= {ProCompPathMax}.", nameof(compIdPath));

        path = default;
        if (!TryResolveMdl(assembly, out var mdl)) return false;

        var ids = new int[ProCompPathMax];
        for (int i = 0; i < compIdPath.Count; i++)
            ids[i] = compIdPath[i];

        var rc = G.ProAsmcomppathInit(mdl, ids, compIdPath.Count, ref path);
        if (IsAsmcompPathMiss(rc)) return false;
        Check.Eval(nameof(G.ProAsmcomppathInit), rc);
        return true;
    }

    private static bool IsAsmcompPathMiss(GNS.ProErrors rc)
        => IsNotFound(rc)
        || rc == GNS.ProErrors.PRO_TK_BAD_INPUTS
        || rc == GNS.ProErrors.PRO_TK_INVALID_ITEM;
}
