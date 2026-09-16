using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- 模型项 (DATA) — 直绑 Pro* ----

    /// <summary>取模型项名字(直绑 ProModelitemNameGet)。无名/不可读返回 null。</summary>
    public string? ModelitemNameGet(ItemRef item)
    {
        if (!TryResolveMdl(item, out var mdl)) return null;
        var mitem = new GNS.pro_model_item { type = (GNS.pro_obj_types)(int)item.Type, id = item.Id, owner = mdl };
        var buf = new char[32];
        int st = (int)G.ProModelitemNameGet(ref mitem, buf);
        if (st < 0) return null;
        Ensure(nameof(G.ProModelitemNameGet), st);
        int len = Array.IndexOf(buf, '\0');
        return len < 0 ? new string(buf) : new string(buf, 0, len);
    }

    /// <summary>模型项改名(直绑 ProModelitemNameSet,动作型,失败抛)。</summary>
    public void ModelitemNameSet(ItemRef item, string name)
    {
        ThrowUtil.IfNullOrWhiteSpace(name);
        if (!TryResolveMdl(item, out var mdl))
            throw new InvalidOperationException($"模型 {item.Model.Name} 不在会话中(ProMdlnameInit 失败,需先 Retrieve/Load)");
        var mitem = new GNS.pro_model_item { type = (GNS.pro_obj_types)(int)item.Type, id = item.Id, owner = mdl };
        // ProName 必须填到 wchar_t[32](含 NUL);数组默认 0 填充
        var buf = new char[32];
        int copyLen = Math.Min(name.Length, 31);
        name.AsSpan(0, copyLen).CopyTo(buf);
        int st = (int)G.ProModelitemNameSet(ref mitem, buf);
        Ensure(nameof(G.ProModelitemNameSet), st);
    }

    /// <summary>查询模型项是否可改名(直绑 ProModelitemNameCanChange)。不可读返回 null。</summary>
    public bool? ModelitemNameCanChange(ItemRef item)
    {
        if (!TryResolveMdl(item, out var mdl)) return null;
        var mitem = new GNS.pro_model_item { type = (GNS.pro_obj_types)(int)item.Type, id = item.Id, owner = mdl };
        int st = (int)G.ProModelitemNameCanChange(ref mitem, out var outCanChange);
        if (st < 0) return null;
        Ensure(nameof(G.ProModelitemNameCanChange), st);
        return outCanChange != 0;
    }

    /// <summary>取模型项默认名(直绑 ProModelitemDefaultnameGet)。不可读返回 null。</summary>
    public string? ModelitemDefaultnameGet(ItemRef item)
    {
        if (!TryResolveMdl(item, out var mdl)) return null;
        var mitem = new GNS.pro_model_item { type = (GNS.pro_obj_types)(int)item.Type, id = item.Id, owner = mdl };
        var buf = new char[32];
        int st = (int)G.ProModelitemDefaultnameGet(ref mitem, buf);
        if (st < 0) return null;
        Ensure(nameof(G.ProModelitemDefaultnameGet), st);
        int len = Array.IndexOf(buf, '\0');
        return len < 0 ? new string(buf) : new string(buf, 0, len);
    }

    /// <summary>删除模型项用户自定义名字(直绑 ProModelitemUsernameDelete,动作型,失败抛)。</summary>
    public void ModelitemUsernameDelete(ItemRef item)
    {
        if (!TryResolveMdl(item, out var mdl))
            throw new InvalidOperationException($"模型 {item.Model.Name} 无法解析");
        var mitem = new GNS.pro_model_item { type = (GNS.pro_obj_types)(int)item.Type, id = item.Id, owner = mdl };
        int st = (int)G.ProModelitemUsernameDelete(ref mitem);
        Ensure(nameof(G.ProModelitemUsernameDelete), st);
    }

    /// <summary>取面类型 int(直绑 ProSurfaceInit + ProSurfaceTypeGet);不可读返回 null。
    /// ProSurface 是 BORROWED handle(库 static),不 free。
    /// <para>3 条 null 早退 emit <c>surface.typeget.{reason}</c> trace
    /// (reason ∈ owner_unresolved / init_notfound / type_failed)。</para></summary>
    public int? SurfaceTypeGet(ItemRef surface)
    {
        if (!TryResolveMdl(surface, out var mdl))
        {
            CreoSdkLog.Trace("surface", "typeget.owner_unresolved",
                new { model = surface.Model.Name, id = surface.Id });
            return null;
        }
        int st = (int)G.ProSurfaceInit(mdl, surface.Id, out var pSurf);
        if (st < 0)
        {
            CreoSdkLog.Trace("surface", "typeget.init_notfound",
                new { model = surface.Model.Name, id = surface.Id, rc = st });
            return null;
        }
        st = (int)G.ProSurfaceTypeGet(pSurf, out var srfType);
        if (st < 0)
        {
            CreoSdkLog.Trace("surface", "typeget.type_failed",
                new { model = surface.Model.Name, id = surface.Id, rc = st });
            return null;
        }
        Ensure(nameof(G.ProSurfaceTypeGet), st);
        return (int)srfType;
    }

    /// <summary>ProNoteURLWstringGet 直绑(wstring out + try/finally ProWstringFree)。</summary>
    public string? NoteUrlGet(ItemRef note)
    {
        if (!TryResolveMdl(note, out var mdl)) return null;
        var noteItem = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)note.Type,
            id = note.Id,
            owner = mdl
        };
        var rc = (GNS.ProErrors)G.ProNoteURLWstringGet(ref noteItem, out IntPtr urlPtr);
        if (IsNotFound(rc)) return null;          // query 语义:note 无 URL
        Check.Eval(nameof(G.ProNoteURLWstringGet), rc);

        if (urlPtr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(urlPtr); }
        finally { G.ProWstringFree(urlPtr); }     // 铁律:Creo 串经 ProWstringFree 释放
    }
}
