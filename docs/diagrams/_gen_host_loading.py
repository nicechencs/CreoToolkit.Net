# One-shot generator for docs/diagrams/host-loading.drawio
# Run from repo root: python docs/diagrams/_gen_host_loading.py
from __future__ import annotations

from pathlib import Path
from xml.sax.saxutils import escape

OUT = Path(__file__).with_name("host-loading.drawio")

S = {
    "creo": "rounded=1;whiteSpace=wrap;html=1;fillColor=#DD6A2B;strokeColor=#B45309;fontColor=#FFFFFF;fontStyle=1;fontFamily=Microsoft YaHei;fontSize=13;arcSize=8;",
    "native": "rounded=1;whiteSpace=wrap;html=1;fillColor=#1B365D;strokeColor=#0F2340;fontColor=#FFFFFF;fontFamily=Microsoft YaHei;fontSize=12;arcSize=8;",
    "clr": "rounded=1;whiteSpace=wrap;html=1;fillColor=#5B2C6F;strokeColor=#3D1B4D;fontColor=#FFFFFF;fontFamily=Microsoft YaHei;fontSize=12;arcSize=8;",
    "host": "rounded=1;whiteSpace=wrap;html=1;fillColor=#3E5FBB;strokeColor=#2C4A96;fontColor=#FFFFFF;fontFamily=Microsoft YaHei;fontSize=12;arcSize=8;",
    "app": "rounded=1;whiteSpace=wrap;html=1;fillColor=#1A7A4C;strokeColor=#0F5A36;fontColor=#FFFFFF;fontFamily=Microsoft YaHei;fontSize=12;arcSize=8;",
    "sdk": "rounded=1;whiteSpace=wrap;html=1;fillColor=#0E7490;strokeColor=#155E75;fontColor=#FFFFFF;fontFamily=Microsoft YaHei;fontSize=12;arcSize=8;",
    "proc": "rounded=1;whiteSpace=wrap;html=1;fillColor=#FFFFFF;strokeColor=#6c8ebf;fontColor=#1B2438;fontFamily=Microsoft YaHei;fontSize=12;arcSize=8;",
    "ok": "rounded=1;whiteSpace=wrap;html=1;fillColor=#E8F6EE;strokeColor=#1A7A4C;fontColor=#0F5A36;fontFamily=Microsoft YaHei;fontSize=12;arcSize=8;",
    "err": "rounded=1;whiteSpace=wrap;html=1;fillColor=#FDECEC;strokeColor=#C0392B;fontColor=#7B1D1D;fontFamily=Microsoft YaHei;fontSize=12;arcSize=8;",
    "note": "rounded=0;whiteSpace=wrap;html=1;fillColor=#F7F8FC;strokeColor=#B9C2D6;fontColor=#3A4560;align=left;verticalAlign=top;fontFamily=Microsoft YaHei;fontSize=11;spacingLeft=8;spacingTop=6;",
    "title": "text;html=1;strokeColor=none;fillColor=none;align=left;verticalAlign=middle;whiteSpace=wrap;fontFamily=Microsoft YaHei;fontSize=20;fontStyle=1;fontColor=#1B2438;",
    "sub": "text;html=1;strokeColor=none;fillColor=none;align=left;verticalAlign=middle;whiteSpace=wrap;fontFamily=Microsoft YaHei;fontSize=12;fontColor=#5A6478;",
    "lane": "rounded=0;whiteSpace=wrap;html=1;fillColor=#EEF1F8;strokeColor=#B9C2D6;fontColor=#1B2438;verticalAlign=top;align=center;fontFamily=Microsoft YaHei;fontSize=12;fontStyle=1;spacingTop=6;",
    "legend": "rounded=1;whiteSpace=wrap;html=1;fontFamily=Microsoft YaHei;fontSize=11;arcSize=8;fontColor=#1B2438;",
}
DEC = "rhombus;whiteSpace=wrap;html=1;fillColor=#FBEADC;strokeColor=#DD6A2B;fontColor=#1B2438;fontFamily=Microsoft YaHei;fontSize=11;"
EDGE = "edgeStyle=orthogonalEdgeStyle;rounded=1;orthogonalLoop=1;jettySize=auto;html=1;endArrow=block;endFill=1;strokeColor=#3A4560;strokeWidth=1.4;fontFamily=Microsoft YaHei;fontSize=10;fontColor=#3A4560;"
EDGE_ERR = EDGE + "strokeColor=#C0392B;dashed=1;"
EDGE_OK = EDGE + "strokeColor=#1A7A4C;"
EDGE_DASH = EDGE + "dashed=1;dashPattern=8 8;strokeColor=#6c8ebf;"


class Page:
    def __init__(self, pid: str, name: str, w: int, h: int):
        self.pid = pid
        self.name = name
        self.w = w
        self.h = h
        self.cells: list[str] = []
        self.n = 2

    def _id(self) -> str:
        i = f"{self.pid}_{self.n}"
        self.n += 1
        return i

    def box(self, x, y, w, h, text, style="proc") -> str:
        cid = self._id()
        st = S[style] if style in S else style
        val = escape(text, {'"': "&quot;", "'": "&apos;"}).replace("\n", "&lt;br/&gt;")
        self.cells.append(
            f'<mxCell id="{cid}" value="{val}" style="{st}" vertex="1" parent="1">'
            f'<mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry"/></mxCell>'
        )
        return cid

    def edge(self, src, dst, label="", style=None, exit_x=None, exit_y=None, entry_x=None, entry_y=None) -> str:
        cid = self._id()
        st = style or EDGE
        extra = ""
        if exit_x is not None:
            extra += f"exitX={exit_x};exitY={exit_y if exit_y is not None else 1};exitDx=0;exitDy=0;"
        if entry_x is not None:
            extra += f"entryX={entry_x};entryY={entry_y if entry_y is not None else 0};entryDx=0;entryDy=0;"
        val = escape(label, {'"': "&quot;", "'": "&apos;"}) if label else ""
        self.cells.append(
            f'<mxCell id="{cid}" value="{val}" style="{st}{extra}" edge="1" parent="1" source="{src}" target="{dst}">'
            f'<mxGeometry relative="1" as="geometry"/></mxCell>'
        )
        return cid

    def xml(self) -> str:
        body = "\n        ".join(self.cells)
        return f'''  <diagram id="{self.pid}" name="{escape(self.name)}">
    <mxGraphModel dx="1200" dy="800" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="{self.w}" pageHeight="{self.h}" math="0" shadow="0">
      <root>
        <mxCell id="0"/>
        <mxCell id="1" parent="0"/>
        {body}
      </root>
    </mxGraphModel>
  </diagram>'''


def page_overview() -> Page:
    p = Page("overview", "1. 总览 · 进程内加载链", 1600, 1680)
    p.box(40, 20, 900, 36, "CreoToolkit 进程内加载链（生产路径：InitializeApp）", "title")
    p.box(40, 58, 1100, 28, "运行时真正触达 Creo 的 native DLL 只有一颗 CreoToolkit.NativeHost.dll。托管 P/Invoke 绑回同一模块。不部署 nethost / hostfxr / runtimeconfig.json。", "sub")

    # lanes
    p.box(40, 100, 280, 1420, "启动器 / Creo", "lane")
    p.box(340, 100, 380, 1420, "NativeHost.dll（C++ · L1 ABI 编入）", "lane")
    p.box(740, 100, 380, 1420, "CLR 4 默认 AppDomain", "lane")
    p.box(1140, 100, 400, 1420, "业务 App / 命令就绪", "lane")

    a1 = p.box(60, 160, 240, 70, "start-*.bat\nlaunch.ps1", "creo")
    a2 = p.box(60, 260, 240, 90, "物化 WorkDir/protk.dat\nexec_file → NativeHost.dll\ntext_dir → apps/<app>/text", "proc")
    a3 = p.box(60, 380, 240, 90, "注入 CTK_HOST_METHOD=InitializeApp\nCTK_HOST_ASSEMBLY / CTK_APP_*\n可选 -Model → LOAD_MODEL_PATH", "proc")
    a4 = p.box(60, 500, 240, 70, "Creo Parametric 启动\n读 protk.dat（startup dll）", "creo")
    p.edge(a1, a2)
    p.edge(a2, a3)
    p.edge(a3, a4)

    n1 = p.box(370, 500, 320, 70, "LoadLibrary\nCreoToolkit.NativeHost.dll", "native")
    n2 = p.box(370, 610, 320, 90, "DllMain PROCESS_ATTACH\n存 HINSTANCE + FNV bootstrapId\n写 ctk-attach-p{pid}.log\n（loader lock：禁堆/线程/LoadLibrary）", "native")
    n3 = p.box(370, 740, 320, 90, "user_initialize（Creo 主线程）\n重入守卫 · session 路径 · JSONL\nctk_toolkit_touch\nSetDllDirectory + CTK_HOST_NATIVE_DLL", "native")
    p.edge(a4, n1)
    p.edge(n1, n2)
    p.edge(n2, n3)

    c1 = p.box(770, 740, 320, 90, "mscoree / CLRCreateInstance\nGetRuntime(v4.0.30319)\nICLRRuntimeHost::Start\n已在跑则附加，永不 Stop", "clr")
    c2 = p.box(770, 870, 320, 80, "ExecuteInDefaultAppDomain\nCreoToolkit.Host.dll\nBootstrap.InitializeApp(string)", "clr")
    p.edge(n3, c1)
    p.edge(c1, c2)

    h1 = p.box(770, 990, 320, 80, "RunEntry\nAssemblyResolve 挂钩\n绑 host-managed.log / session.start", "host")
    h2 = p.box(770, 1110, 320, 80, "NativeHostModule.EnsureLoaded\nCreoSession.Attach\nCtkAbi.Verify（首个真实 P/Invoke）", "sdk")
    h3 = p.box(770, 1230, 320, 70, "[可选] Diagnostics.PrepareSession\n模型预载 / 打开窗口", "host")
    p.edge(c2, h1)
    p.edge(h1, h2)
    p.edge(h2, h3)

    ap1 = p.box(1160, 1230, 360, 80, "CreoApplicationLoader\nLoadFrom(CTK_APP_ASSEMBLY)\n或内置 NoOpApp", "app")
    ap2 = p.box(1160, 1350, 360, 80, "CreoAppHost.Run\n命令/菜单/Ribbon 注册\nOnInitialize", "app")
    ap3 = p.box(1160, 1470, 360, 50, "返回 0 · 命令就绪 · 等待点击", "ok")
    p.edge(h3, ap1)
    p.edge(ap1, ap2)
    p.edge(ap2, ap3)

    # pinvoke return
    pv = p.box(370, 1350, 320, 90, "P/Invoke 回程\nNativeMethods.Ctk_* / Pro*\nDllImport(\"CreoToolkit.NativeHost\")\n→ 已加载的同一 DLL", "note")
    p.edge(ap2, pv, "命令回调 / ABI", EDGE_DASH, exit_x=0, exit_y=0.5, entry_x=1, entry_y=0.5)

    p.box(60, 620, 240, 200, "启动器注入的关键环境变量\n\n• CTK_HOST_METHOD\n• CTK_HOST_ASSEMBLY\n• CTK_APP_ASSEMBLY + TYPE\n• CTK_APP_ALLOWED_ROOTS\n• CTK_APP_MSG_FILE\n• CTK_BOOTSTRAP_LOG_DIR", "note")
    p.box(60, 860, 240, 160, "不在这条链上\n\n• 无 sidecar Native.Creo.dll\n• 无 CoreCLR / nethost\n• 无 collectible ALC\n• 不 Stop CLR、不 FreeLibrary", "note")
    return p


def page_native() -> Page:
    p = Page("native", "2. Native · user_initialize", 1500, 1980)
    p.box(40, 20, 800, 36, "Native 入口：DllMain → user_initialize → CLR", "title")
    p.box(40, 56, 1200, 28, "源码：src/CreoToolkit.NativeHost/src/host_entry.cpp · clr_host_loader.cpp。Creo 对 user_initialize 非零通常不再调 user_terminate。", "sub")

    d0 = p.box(520, 100, 280, 50, "Creo LoadLibrary NativeHost.dll", "creo")
    d1 = p.box(520, 180, 280, 80, "DllMain PROCESS_ATTACH\n存 g_hinst_dll\nFNV-1a bootstrapId\n一条 Kernel32 attach 证据", "native")
    p.edge(d0, d1)

    u0 = p.box(520, 300, 280, 50, "user_initialize（Creo 主线程）", "native")
    p.edge(d1, u0, "Creo 识别 TOOLKIT DLL")

    g = p.box(500, 390, 320, 70, "重入守卫 g_user_init_state\n0 未进入 / 1 已进入 / 2 成功", DEC)
    p.edge(u0, g)

    re_ok = p.box(900, 360, 220, 50, "成功后重入 → 返回 0\n（双 protk.dat 条目）", "ok")
    re_fail = p.box(900, 430, 220, 50, "失败后重入 → 返回 -14\n不重跑、不恢复状态", "err")
    p.edge(g, re_ok, "state=2", EDGE_OK, exit_x=1, exit_y=0.35, entry_x=0, entry_y=0.5)
    p.edge(g, re_fail, "state=1", EDGE_ERR, exit_x=1, exit_y=0.7, entry_x=0, entry_y=0.5)

    s1 = p.box(500, 520, 320, 90, "init_session_paths\nretention 清理过期 JSONL/pointer\n回写 CTK_BOOTSTRAP_LOG_DIR\ninit_trace_context + 可选 ETW", "proc")
    p.edge(g, s1, "state=0 → 置 1")

    t = p.box(500, 650, 320, 60, "ctk_toolkit_touch()\nProToolkitApplTextPathGet", "proc")
    p.edge(s1, t)
    tfail = p.box(900, 650, 240, 50, "touch 非零\nfinish_user_initialize(rc)", "err")
    p.edge(t, tfail, "失败", EDGE_ERR, exit_x=1, exit_y=0.5, entry_x=0, entry_y=0.5)

    pub = p.box(500, 750, 320, 70, "publish_native_host_path()\nCTK_HOST_NATIVE_DLL\nSetDllDirectory + PATH", "proc")
    p.edge(t, pub, "0")

    m = p.box(500, 860, 320, 70, "initialize_method()\n读 CTK_HOST_METHOD\n缺省 InitializeApp", DEC)
    p.edge(pub, m)
    mfail = p.box(900, 870, 240, 50, "未知方法 → -13\n无静默回落", "err")
    p.edge(m, mfail, "非法", EDGE_ERR, exit_x=1, exit_y=0.5, entry_x=0, entry_y=0.5)

    inv = p.box(500, 980, 320, 50, "invoke_managed(method)", "native")
    p.edge(m, inv, "合法")

    path = p.box(500, 1070, 320, 70, "解析 CreoToolkit.Host.dll\nCTK_HOST_ASSEMBLY 或\npackaged/dev 双布局探测", DEC)
    p.edge(inv, path)
    p10 = p.box(900, 1070, 240, 50, "路径失败 → -10", "err")
    p.edge(path, p10, "缺文件", EDGE_ERR, exit_x=1, exit_y=0.5, entry_x=0, entry_y=0.5)

    clr = p.box(500, 1180, 320, 110, "clr_host::execute\nCLRCreateInstance(CLSID_CLRMetaHost)\nGetRuntime(v4.0.30319)\nIsLoadable / GetInterface\nICLRRuntimeHost::Start\nExecuteInDefaultAppDomain", "clr")
    p.edge(path, clr, "找到")
    p12 = p.box(900, 1210, 240, 50, "Execute 失败 → -12", "err")
    p.edge(clr, p12, "executed=false", EDGE_ERR, exit_x=1, exit_y=0.5, entry_x=0, entry_y=0.5)

    ret = p.box(500, 1330, 320, 60, "托管入口返回 rc\n写 clr.entry_returned", DEC)
    p.edge(clr, ret, "executed")
    ok = p.box(500, 1440, 320, 50, "rc=0 → g_user_init_state=2\nfinish 直返 0", "ok")
    bad = p.box(900, 1440, 240, 70, "rc≠0 → finish_user_initialize\n关 ETW / 写 session.end\nCreo 通常不再 terminate", "err")
    p.edge(ret, ok, "0", EDGE_OK)
    p.edge(ret, bad, "非零（含托管 -1）", EDGE_ERR, exit_x=1, exit_y=0.5, entry_x=0, entry_y=0.5)

    p.box(40, 1560, 1420, 140, "返回码速查（native 侧）\n\n"
          "0 成功　　-10 Host.dll 路径解析失败　　-12 ExecuteInDefaultAppDomain 失败　　-13 CTK_HOST_METHOD 非法\n"
          "-14 user_initialize 失败后重入　　托管异常 / 幂等拒绝 通常回 -1\n\n"
          "DllMain 只做 Kernel32：禁止堆、线程、LoadLibrary、递归 mkdir。历史上若未链接 ucore，Creo 可能跑完 DllMain 却永不调 user_initialize。", "note")

    p.box(40, 1720, 1420, 200, "常量（host_entry.cpp）\n\n"
          "kManagedAssembly = CreoToolkit.Host.dll\n"
          "kTypeName        = CreoToolkit.Host.Bootstrap\n"
          "kDefaultMethod   = InitializeApp\n"
          "kTerminateMethod = Terminate\n\n"
          "允许的 CTK_HOST_METHOD：Initialize | InitializeAndAttach | InitializeApp。未设置 → InitializeApp。", "note")
    return p


def page_managed() -> Page:
    p = Page("managed", "3. Managed · Bootstrap 入口", 1580, 1760)
    p.box(40, 20, 900, 36, "托管入口：Bootstrap 三方法 + InitializeApp 生产路径", "title")
    p.box(40, 56, 1300, 28, "源码：src/CreoToolkit.Host/Bootstrap.cs。四个入口都是 public static int Method(string)，以满足 ExecuteInDefaultAppDomain。", "sub")

    run = p.box(560, 100, 360, 70, "RunEntry(name)\nEnsureSharedAssemblyResolve\n绑日志 → body", "host")

    d = p.box(560, 210, 360, 70, "CTK_HOST_METHOD ?", DEC)
    p.edge(run, d)

    i1 = p.box(80, 330, 280, 90, "Initialize\n只挂钩 AssemblyResolve\nbody → 0\n无 Attach / 无命令", "proc")
    i2 = p.box(430, 330, 280, 90, "InitializeAndAttach\nEnsureLoaded + Attach\nCtkAbi.Verify\n无 ICreoApplication", "sdk")
    i3 = p.box(800, 330, 320, 90, "InitializeApp（生产默认）\nAttach + 装 App + Run\n可选 Diagnostics", "host")
    i4 = p.box(1180, 330, 280, 90, "Terminate\n仅 user_terminate 调用\nDisposeManagedHost\n不卸载程序集", "err")
    p.edge(d, i1, "Initialize", None, exit_x=0, exit_y=0.5, entry_x=0.5, entry_y=0)
    p.edge(d, i2, "InitializeAndAttach")
    p.edge(d, i3, "缺省 / InitializeApp", None, exit_x=1, exit_y=0.35, entry_x=0.5, entry_y=0)
    # terminate is not from initialize_method; note it
    p.box(1180, 210, 280, 70, "user_terminate 专用\n不在 initialize_method 白名单", "note")
    p.edge(i4, i4, "", EDGE_DASH)  # no, don't self-loop
    # remove that accidental self edge - actually I just created a self-loop. Let me not do that.
    # Wait I already appended it. I need to not call that. Too late in this function if I already did...
    # I'll rebuild - actually I'll just not include terminate as from decision. The self-loop is bad.
    # Let me rewrite this page without the self-edge. I'll fix by not calling p.edge(i4,i4).
    # I already did. I need to pop the last cell.
    p.cells.pop()

    # InitializeApp detail
    p.box(40, 460, 1500, 28, "InitializeApp 逐步（生产路径）", "title")

    a0 = p.box(80, 510, 240, 60, "_appHost != null ?\n重入拒绝返回 -1", DEC)
    a1 = p.box(400, 510, 260, 60, "EnsureSession()\nAttach + CtkAbi.Verify", "sdk")
    a2 = p.box(740, 510, 280, 60, "DiagnosticsActivation\nFromEnvironment()", DEC)
    p.edge(i3, a0)
    p.edge(a0, a1, "否")
    rej = p.box(80, 620, 240, 50, "已初始化 → -1\n不静默覆盖", "err")
    p.edge(a0, rej, "是", EDGE_ERR, exit_x=0.5, exit_y=1, entry_x=0.5, entry_y=0)
    p.edge(a1, a2)

    prep = p.box(740, 620, 280, 70, "PrepareSession\n预载/打开模型\n失败且 REQUIRED → 清理后非 0", "host")
    skip = p.box(1100, 620, 240, 50, "未请求则跳过", "ok")
    p.edge(a2, prep, "Requested", EDGE_OK)
    p.edge(a2, skip, "off", None, exit_x=1, exit_y=0.5, entry_x=0, entry_y=0.5)

    load = p.box(400, 740, 280, 70, "HostStartupOptions\nCreoApplicationLoader\nCreateLoadedFromEnvironment", "app")
    p.edge(prep, load)
    p.edge(skip, load, "", EDGE_DASH, exit_x=0.5, exit_y=1, entry_x=1, entry_y=0.5)

    runh = p.box(760, 740, 280, 70, "CreoAppHost.ForHost\n+ Run()\n命令/菜单/OnInitialize", "app")
    p.edge(load, runh)

    post = p.box(760, 860, 280, 70, "[可选] RunAppActions\nCTK_APP_RUN_COMMAND\n诊断 Ribbon", "host")
    p.edge(runh, post)

    done = p.box(760, 970, 280, 50, "返回 0", "ok")
    p.edge(post, done)

    fail = p.box(1140, 740, 300, 160, "任意异常\n\n写 managed-init-failure.txt\nDisposeManagedHost()\n返回 -1\n\n（Initialize / Terminate\n失败不二次 Dispose）", "err")
    p.edge(runh, fail, "抛", EDGE_ERR, exit_x=1, exit_y=0.5, entry_x=0, entry_y=0.3)

    p.box(40, 1080, 1500, 220, "Diagnostics 开关（DiagnosticsActivation）\n\n"
          "• CTK_DIAGNOSTICS_ENABLED=0/off/false/no → 强制关，即使遗留变量仍在。\n"
          "• 否则若 ENABLED=on、REQUIRED=on，或遗留 CTK_APP_PRELOAD_MODEL / LOAD_MODEL_PATH / RUN_COMMAND / LOAD_RIBBON 非空 → Requested。\n"
          "• launch.ps1 仅在传入 -Model 或已有 CTK_HOST_SMOKE_MODEL / CTK_APP_LOAD_MODEL_PATH 时预载模型并因此打开诊断。\n"
          "• PrepareSession / RunAppActions 标了 NoInlining：Diagnostics.dll 缺失时不毒化 JIT；REQUIRED=1 才把可选失败升级为启动失败。\n"
          "• Host 对 Diagnostics 有硬项目引用，Stage 总会拷贝该 DLL。", "note")

    p.box(40, 1320, 1500, 180, "EnsureSession 细节\n\n"
          "1. 必须在 Creo initialize 线程（CreoThread）\n"
          "2. 再次 EnsureSharedAssemblyResolve\n"
          "3. NativeHostModule.EnsureLoaded：SetDllDirectoryW + AddDllDirectory + LoadLibraryW\n"
          "   目标 CTK_HOST_NATIVE_DLL 或 ../native/CreoToolkit.NativeHost.dll（进程内已加载的同一 DLL，非第二份插件）\n"
          "4. CreoSession.Attach() → CtkAbi.Verify 逐项校核结构体尺寸/offset（首个真实 P/Invoke）", "note")

    p.box(40, 1520, 1500, 180, "in-tree 触发对照\n\n"
          "Initialize / InitializeAndAttach：无 start-*.bat 设置；仅测试或手工环境变量。\n"
          "InitializeApp + NoOpApp：两套 CTK_APP_* 都空。deploy launcher 总会设置它们，故 start.bat 不是这条。\n"
          "InitializeApp + Core：start.bat / start-protk-samples-core.bat → ProtkSimpleSamplesCoreApp。\n"
          "InitializeApp + WinForms：start-protk-samples-winforms.bat → ProtkSimpleSamplesApp。", "note")
    return p


def page_appload() -> Page:
    p = Page("appload", "4. 应用装配 · LoadFrom", 1580, 1680)
    p.box(40, 20, 900, 36, "应用装配：NoOpApp / LoadFrom / AssemblyResolve / SampleCatalog", "title")
    p.box(40, 56, 1300, 28, "源码：CreoApplicationLoader.cs。Framework 无 collectible ALC；插件与 Host/Sdk/Interop 同在默认 AppDomain。", "sub")

    d = p.box(580, 110, 320, 80, "CTK_APP_ASSEMBLY\n与 CTK_APP_TYPE ?", DEC)

    noop = p.box(80, 250, 300, 90, "都空 → new NoOpApp()\n空命令集，闭环 attach/lifecycle\n不 LoadFrom", "ok")
    half = p.box(460, 250, 300, 90, "只设一个 → 抛 InvalidOperation\n必须成对出现", "err")
    both = p.box(900, 250, 420, 90, "都设 → 解析路径（相对 BaseDirectory）\nallowlist：BaseDirectory + CTK_APP_ALLOWED_ROOTS\nFile.Exists → Assembly.LoadFrom", "app")
    p.edge(d, noop, "都空", None, exit_x=0, exit_y=0.5, entry_x=0.5, entry_y=0)
    p.edge(d, half, "缺一")
    p.edge(d, both, "都设", None, exit_x=1, exit_y=0.5, entry_x=0.5, entry_y=0)

    t1 = p.box(900, 380, 420, 80, "GetType(throwOnError: true)\n必须实现 ICreoApplication\n必须有 public 无参构造", "proc")
    t2 = p.box(900, 500, 420, 50, "Activator.CreateInstance → App", "app")
    p.edge(both, t1)
    p.edge(t1, t2)

    rej = p.box(900, 590, 420, 70, "路径不在 allowlist → REJECT\n文件不存在 → FileNotFound\n类型不对 → InvalidOperation", "err")
    p.edge(both, rej, "校验失败", EDGE_ERR, exit_x=1, exit_y=0.5, entry_x=1, entry_y=0.5)

    p.box(40, 700, 1500, 36, "AssemblyResolve（进程只挂钩一次）", "title")

    r0 = p.box(80, 760, 260, 70, "Fusion ApplicationBase\n= xtop.exe 目录\n看不到 managed/", "note")
    r1 = p.box(400, 760, 260, 70, "跳过 *.resources", "proc")
    r2 = p.box(720, 760, 280, 70, "已加载且名称匹配\n→ 复用同一份\n避免两份 ICreoApplication", "ok")
    r3 = p.box(1060, 760, 360, 90, "否则在命名目录 LoadFrom\nHost / Sdk / Interop /\nCTK_HOST_ASSEMBLY 目录\n不枚举所有 DLL", "host")
    p.edge(r0, r1)
    p.edge(r1, r2)
    p.edge(r2, r3, "未命中")

    p.box(40, 900, 1500, 36, "Sample 聚合（编译期目录，不是运行时扫描）", "title")

    s0 = p.box(80, 960, 320, 90, "samples/SampleCatalog.props\n唯一人工真源\nCtkSampleModule × 24\nCtkSampleApp × 2", "app")
    s1 = p.box(460, 960, 320, 90, "MSBuild 生成\nSampleModuleCatalog.g.cs\n+ Core/WinForms ProjectReference", "proc")
    s2 = p.box(840, 960, 320, 90, "CoreApp.Initialize\n→ ProtkSamplesCoreRegistration\n→ RegisterAll（按 Order）", "app")
    s3 = p.box(1220, 960, 280, 90, "WinForms 再追加\n7 个 dialog launcher\n+ CreoToolkit 顶级菜单", "app")
    p.edge(s0, s1)
    p.edge(s1, s2)
    p.edge(s2, s3)

    p.box(40, 1100, 1500, 240, "Stage 产物与加载对应关系\n\n"
          "deploy/native/CreoToolkit.NativeHost.dll     ← Creo LoadLibrary（唯一 native）\n"
          "deploy/native/protk.host.dat                 ← 模板；launcher 物化成 WorkDir/protk.dat\n"
          "deploy/managed/CreoToolkit.Host.dll          ← ExecuteInDefaultAppDomain\n"
          "deploy/managed/{Sdk,Interop,App,Diagnostics}.dll  ← AssemblyResolve 共享副本\n"
          "deploy/apps/protk-samples-core/*.dll         ← CTK_APP_ASSEMBLY（Core）\n"
          "deploy/apps/protk-samples-winforms/*.dll     ← CTK_APP_ASSEMBLY（WinForms）\n\n"
          "Terminate 不卸载程序集。同进程不承诺再 LoadFrom 另一个 app（InitializeApp 重入 -1）。要换 app 就重启 Creo。", "note")

    p.box(40, 1360, 1500, 260, "默认 AppDomain 驻留内容（进程级）\n\n"
          "Host · 可选 Diagnostics · App 契约 · Sdk · Interop · Polyfills · Serilog · 外部 ICreoApplication 及其 Module\n\n"
          "不在主加载链、按 app 需要加载：CreoToolkit.Agent / Agent.Client（命名管道 verb）。\n\n"
          "P/Invoke 库名永远是 CreoToolkit.NativeHost（已由 Creo 注册加载的那一颗）。"
          "exports.def 再导出约 5900 个 Pro*，让 Generated Pro*.g.cs 解析到同一 DLL。", "note")
    return p


def page_run() -> Page:
    p = Page("apprun", "5. CreoAppHost.Run · 命令注册", 1500, 1680)
    p.box(40, 20, 900, 36, "CreoAppHost.Run：命令硬失败、菜单可降级", "title")
    p.box(40, 56, 1300, 28, "源码：src/CreoToolkit.App/CreoAppHost.cs。必须在 Creo 主线程；Run 只能尝试一次。", "sub")

    r0 = p.box(560, 110, 300, 50, "CreoAppHost.Run()", "app")
    r1 = p.box(560, 190, 300, 60, "app.Initialize(builder)\n只声明命令/菜单/Ribbon", "proc")
    r2 = p.box(560, 280, 300, 50, "builder.BuildDefinition()\n校验菜单 commandName", "proc")
    r3 = p.box(560, 360, 300, 70, "CommandDispatchPin\n钉住单一 native dispatch\nCtk_CommandBridgeInitialize", "native")
    r4 = p.box(560, 460, 300, 50, "可选 CommandCapacityReserve", "proc")
    r5 = p.box(540, 540, 340, 70, "CommandRegistrationRuntime\nCtk_CommandActionAdd +\nProCmdDesignate（硬失败）", "native")
    r6 = p.box(540, 650, 340, 80, "菜单外观（可降级 WARN）\n顶级菜单 → 子菜单 → 按钮\n→ option/check/radio → Ribbon", "proc")
    r7 = p.box(540, 770, 340, 70, "session≠null → OnInitialize(ctx)\n成功后 _lifecycleInitialized=true", "app")
    r8 = p.box(560, 880, 300, 50, "host.init.ok · 等待点击", "ok")
    p.edge(r0, r1)
    p.edge(r1, r2)
    p.edge(r2, r3)
    p.edge(r3, r4)
    p.edge(r4, r5)
    p.edge(r5, r6)
    p.edge(r6, r7)
    p.edge(r7, r8)

    p.box(40, 190, 420, 200, "ICreoApplication 生命周期\n\n"
          "1. Initialize(builder) — 声明期，不要持有短命 native 资源\n"
          "2. Run 注册成功后 OnInitialize — 可订阅事件 / 启资源\n"
          "3. 仅 OnInitialize 完整返回后，停止时才配对 OnTerminate\n"
          "4. OnTerminate 在 BridgeTerminate 与 Session.Dispose 之前，session 仍可用", "note")

    p.box(980, 190, 460, 280, "失败点回滚（F1–F5）\n\n"
          "F1 Initialize 抛：无资源 → Dispose session\n"
          "F2 BridgeInitialize 失败：BridgeTerminate → pin → registry\n"
          "F3 第 N 条 CommandActionAdd 失败：同 F2（前 N-1 已进 Creo 路由表）\n"
          "F4 菜单失败：命令已成功；WARN 继续，不撤销命令\n"
          "F5 OnInitialize 抛：不调 OnTerminate（与构造器异常语义一致）\n\n"
          "F5 前 app 自己产生的副作用必须自行回滚。", "note")

    p.box(980, 500, 460, 200, "命令回调返回码（不让托管异常穿过 native）\n\n"
          "0 成功（默认 message bar 回显 [ctk] done: <cmd>）\n"
          "1 handler 抛异常（LastFailedToken 记录 token）\n"
          "2 命令名/token 未注册\n\n"
          "CTK_DISPATCH_ECHO=0 可关回显。", "note")

    p.box(40, 980, 1420, 280, "WinForms vs Core 注册差异\n\n"
          "两者都走 SampleModuleCatalog.RegisterAll（24 个模块，Order 锁定）。\n\n"
          "Core：模块自带的原生 Creo 菜单（如 ExtCreoMenu）仍注册；没有 7 个主题 dialog。适合 nogui / 集成。\n"
          "WinForms：同样 RegisterAll，再加 7 个 ctk.dialog.* launcher + MetadataCatalogBuilder + WinForms/WPF 桥 + MenuAdd(\"CreoToolkit\"）。\n"
          "命令元数据 116 条普通命令（109 模块 + 7 launcher）；ExtCreoMenu 的 option command 另计。\n\n"
          "CreoAppBuilder.LoadCustomRibbon 与 CTK_APP_LOAD_RIBBON 最终都调 ProRibbonDefinitionfileLoad。"
          "当前 ExtCreoMenu sample 跳过二进制 .rbn，默认 sample 不能称为「已装自定义 Ribbon」。", "note")

    p.box(40, 1280, 1420, 320, "点击之后（加载完成后的运行时，非启动链）\n\n"
          "Creo 菜单点击 → NativeHost 内 SEH trampoline（Ctk_CommandActionAdd）\n"
          "→ 钉住的 CommandDispatchPin 函数指针\n"
          "→ CreoAppHost 按 token 找到 handler\n"
          "→ SessionCommandDispatcher 保证主线程\n"
          "→ ICreoApplication 注册的 lambda / Module Registration\n"
          "→ L3 Sdk 对象模型 → L2 P/Invoke → 同一颗 NativeHost.dll 内 Pro* / Ctk_*\n\n"
          "Agent 路径（可选）：外部进程 NamedPipeAgentClient → NamedPipeAgentServer → MainThreadAgentExecutor → CreoCommandRouter。", "note")
    return p


def page_term() -> Page:
    p = Page("terminate", "6. 停止 · user_terminate", 1500, 1580)
    p.box(40, 20, 800, 36, "停止与卸载：处置对象，不卸 CLR / 不卸 DLL", "title")
    p.box(40, 56, 1300, 28, "源码：host_entry.cpp user_terminate · Bootstrap.Terminate / DisposeManagedHost · CreoAppHost.Dispose。", "sub")

    t0 = p.box(560, 110, 300, 50, "Creo 调用 user_terminate", "creo")
    g = p.box(540, 190, 340, 60, "重入守卫 g_user_terminate_entered\n第二次 → WARN no-op", DEC)
    p.edge(t0, g)
    skip = p.box(960, 190, 240, 50, "重复调用直接返回", "ok")
    p.edge(g, skip, "已进入", EDGE_ERR, exit_x=1, exit_y=0.5, entry_x=0, entry_y=0.5)

    clr = p.box(540, 290, 340, 60, "clr_host::is_started() ?", DEC)
    p.edge(g, clr, "首次")
    nos = p.box(960, 290, 280, 50, "从未 Start → 跳过托管 Terminate\n避免退出路径冷启动 CLR", "note")
    p.edge(clr, nos, "否", None, exit_x=1, exit_y=0.5, entry_x=0, entry_y=0.5)

    inv = p.box(540, 390, 340, 50, "invoke_managed(\"Terminate\")", "clr")
    p.edge(clr, inv, "是")

    d0 = p.box(540, 480, 340, 70, "DisposeManagedHost\n必须在 Creo initialize 线程\n拒绝则两边 owner 都保留", "host")
    p.edge(inv, d0)

    d1 = p.box(540, 590, 340, 90, "appHost.Dispose()\nOnTerminate（仅 _lifecycleInitialized）\nBridgeTerminate + pin.Dispose\n清 _cmds", "app")
    d2 = p.box(540, 720, 340, 70, "session.Dispose()\nCallbackRegistry / 句柄\n主线程释放门 / adopted handle", "sdk")
    d3 = p.box(540, 830, 340, 50, "静态字段置 null", "proc")
    p.edge(d0, d1)
    p.edge(d1, d2)
    p.edge(d2, d3)

    n1 = p.box(540, 920, 340, 90, "native：可选 ETW shutdown\n写 session.end\n不 ICLRRuntimeHost::Stop\n不 FreeLibrary NativeHost.dll", "native")
    p.edge(d3, n1)
    p.edge(nos, n1, "继续 native 收尾", EDGE_DASH, exit_x=0.5, exit_y=1, entry_x=1, entry_y=0.3)

    det = p.box(540, 1050, 340, 70, "DLL_PROCESS_DETACH\nloader lock 下不做 IO\nNativeHost 按进程寿命设计", "native")
    p.edge(n1, det)

    p.box(40, 190, 440, 280, "明确不做的事\n\n"
          "• 不 Stop CLR 4（一进程一套，可能已有其他宿主）\n"
          "• 不卸载默认 AppDomain 程序集\n"
          "• NativeHost.dll 不主动 FreeLibrary\n"
          "• 无命令 unregister/re-register 契约\n"
          "• 不承诺同进程热重载\n"
          "• 多实例会互相覆盖 CTK_HOST_*（不支持）", "note")

    p.box(960, 390, 480, 280, "init 失败 vs 正常停止\n\n"
          "Creo 对 user_initialize 非零通常不调 user_terminate。\n"
          "因此 RunEntry（除 Initialize/Terminate）失败时\n"
          "必须自己 DisposeManagedHost，否则泄漏。\n\n"
          "finish_user_initialize(非0) 会关 ETW、写 session.end。\n"
          "托管 Terminate 失败不阻断 native 收尾。", "note")

    p.box(40, 1160, 1420, 340, "证据与日志落点（加载/停止可观测性）\n\n"
          "DllMain attach：%LOCALAPPDATA%\\CreoToolkit\\ctk-attach-p{pid}.log（单条 JSONL，无 traceId）\n"
          "user_initialize JSONL / pointer：CTK_BOOTSTRAP_LOG_DIR（launcher 设为 WorkDir\\logs）\n"
          "托管文件日志：CTK_HOST_MANAGED_LOG → WorkDir\\logs\\host-managed.log（session.start 边界）\n"
          "托管总开关：CTK_DOTNET_LOG / LEVEL / FORMAT / FILE\n"
          "失败兜底：managed-init-failure.txt（与 bootstrap 同目录）\n"
          "handoff ACK：native 仍导出 ctk_native_log_handoff_ack 作 ABI stub；当前 Host 不再 P/Invoke，加载不等待 ack。\n\n"
          "旧 host-marker-*.log / host-native-*.log / CTK_LOG* 已停用或仅迁移期 fallback。", "note")
    return p


def main() -> None:
    pages = [
        page_overview(),
        page_native(),
        page_managed(),
        page_appload(),
        page_run(),
        page_term(),
    ]
    xml = (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<mxfile host="app.diagrams.net" agent="CreoToolkit docs" version="22.1.0" type="device" pages="'
        + str(len(pages))
        + '">\n'
        + "\n".join(pg.xml() for pg in pages)
        + "\n</mxfile>\n"
    )
    OUT.write_text(xml, encoding="utf-8")
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes, {len(pages)} pages)")


if __name__ == "__main__":
    main()
