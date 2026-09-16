/*
 * host_entry.cpp - Creo TOOLKIT DLL 入口，通过 mscoree 宿主 .NET Framework CLR 4。
 *
 * Creo 经 protk.dat 加载本 DLL，在 Creo 主线程调 user_initialize；
 * 本文件保持 native 入口最薄，实际逻辑委托给 CLR 4 内的 CreoToolkit.Host.Bootstrap。
 */
#include <Windows.h>
#include <bcrypt.h>            /* traceId CSPRNG（仅 user_initialize 安全点调用，非 DllMain） */
#include <cstdio>
#include <cstdlib>
#include <cstring>             /* strcmp 用于 ack ABI trace_id 比较 */
#include <cwchar>

#include "host_log_paths.h"
#ifdef CTK_ENABLE_ETW
#include "ctk_etw_provider.h"
#endif
#include "clr_host_loader.h"

#pragma comment(lib, "bcrypt.lib")  /* BCryptGenRandom */

// 触达 Creo TOOLKIT(定义在 toolkit_touch.cpp, 隔离 Creo 头)。强制链接 ucore 运行时, 让 Creo
// 识别本 DLL 为合法 TOOLKIT app 并调用 user_initialize; 否则 DllMain 跑了但 user_initialize 不被调。
extern "C" int ctk_toolkit_touch(void);

namespace {

/* DllMain attach 时一次性算好的 session 级路径，本进程内不变。 */
wchar_t g_host_log_dir[MAX_PATH]       = L"";   /* 例：D:\app\deploy\logs\host */
DWORD   g_host_pid                     = 0;
HINSTANCE g_hinst_dll                  = nullptr; /* DllMain 存下，供 user_initialize 解析路径 */

/* ========== OTel-shaped bootstrap evidence 状态 ========== */
wchar_t g_bootstrap_id[33]      = L"";   /* 32 hex + NUL，DllMain 内 FNV-1a 生成 */
wchar_t g_trace_id[33]          = L"";   /* 32 hex + NUL，user_initialize 内 BCryptGenRandom 生成 */
wchar_t g_bootstrap_jsonl[MAX_PATH] = L"";  /* OTel-shaped JSONL 文件路径 */
volatile LONG g_event_seq       = 0;     /* per-session 事件 seq 自增（OTel 字段 "seq"） */
volatile LONG g_handoff_acked   = 0;     /* atomic flag：handoff ack 是否已收到 */
/* user_initialize 生命周期三态：0=未进入 1=已进入且未成功 2=全链成功。
 * 不论成败只许完整跑一次：同进程重试不可能自愈（全部失败模式都是环境/配置性的）。 */
volatile LONG g_user_init_state = 0;
volatile LONG g_user_terminate_entered = 0; /* 对称守卫：terminate 清理只许跑一次 */
char g_resource_json[256]       = "";    /* OTel resource 预构建 JSON 片段 */

/* 解析 host log 目录（host_log_resolve_dir），缓存到 g_host_log_dir。
 * bootstrap JSONL / pointer / session-index 均落在此目录。 */
void init_session_paths(HINSTANCE hinst_dll)
{
    if (g_host_pid != 0) return; /* 已初始化 */
    g_host_pid = GetCurrentProcessId();

    if (host_log_resolve_dir(hinst_dll, g_host_log_dir, MAX_PATH) != 0) {
        DWORD n = GetTempPathW(MAX_PATH, g_host_log_dir);
        if (n == 0 || n >= MAX_PATH) {
            g_host_log_dir[0] = L'.';
            g_host_log_dir[1] = L'\0';
        } else {
            size_t len = 0;
            while (g_host_log_dir[len] != L'\0') ++len;
            if (len > 0 && (g_host_log_dir[len - 1] == L'\\' || g_host_log_dir[len - 1] == L'/')) {
                g_host_log_dir[len - 1] = L'\0';
            }
        }
    }
}

/* 解析 retention 类型 env：允许 0 与负数（语义 = disable cleanup）。
 * 与 parse_positive_int_env 区别：本函数不把 0/负数当 invalid 回退到 fallback，
 * 而是原样返回，由调用方判断 (<=0 跳过清理)。 */
int parse_int_env_allow_zero_or_negative(const wchar_t* name, int fallback)
{
    wchar_t value[32];
    DWORD n = GetEnvironmentVariableW(name, value, 32);
    if (n == 0 || n >= 32) return fallback;

    bool negative = false;
    DWORD start = 0;
    if (value[0] == L'-') { negative = true; start = 1; }

    int parsed = 0;
    bool any_digit = false;
    for (DWORD i = start; i < n; ++i) {
        if (value[i] < L'0' || value[i] > L'9') return fallback;
        parsed = parsed * 10 + (value[i] - L'0');
        if (parsed > 3650) return fallback;
        any_digit = true;
    }
    if (!any_digit) return fallback;
    return negative ? -parsed : parsed;
}

/* native-bootstrap JSONL 是唯一真源。 */

/* 前向声明：CLR 加载链路诊断事件依赖。 */
int write_otel_event(const char* event_name,
                     int severity_number,
                     const char* severity_text,
                     const char* body_utf8);

/* 组 "<prefix> rc=0x%08X (%d)" body 并写 JSONL 事件（CLR/HRESULT 错误码承接）。 */
void write_otel_event_hr(const char* event_name,
                         int severity_number,
                         const char* severity_text,
                         const char* prefix,
                         int rc)
{
    char body[256];
    _snprintf_s(body, sizeof(body), _TRUNCATE, "%s rc=0x%08X (%d)",
                prefix, (unsigned int)rc, rc);
    write_otel_event(event_name, severity_number, severity_text, body);
}

/* path_utf8 缓冲统一 784 = MAX_PATH(260) × UTF-8 最坏 3 字节 + 余量；
 * 384 时代 CJK 深路径转换失败会把整条路径证据清空。 */
constexpr size_t kPathUtf8Cap = 784;

/* 组 "<prefix> <宽路径转UTF-8>" body 并写 JSONL 事件。 */
void write_otel_event_wpath(const char* event_name,
                            int severity_number,
                            const char* severity_text,
                            const char* prefix,
                            const wchar_t* wpath)
{
    char path_utf8[kPathUtf8Cap];
    int n = WideCharToMultiByte(CP_UTF8, 0, wpath, -1,
                                path_utf8, (int)sizeof(path_utf8), nullptr, nullptr);
    if (n <= 0) path_utf8[0] = '\0';
    char body[896];
    _snprintf_s(body, sizeof(body), _TRUNCATE, "%s %s", prefix, path_utf8);
    write_otel_event(event_name, severity_number, severity_text, body);
}

wchar_t cached_managed_assembly[MAX_PATH] = L"";

constexpr const wchar_t* kManagedAssembly = L"CreoToolkit.Host.dll";
constexpr const wchar_t* kTypeName = L"CreoToolkit.Host.Bootstrap";
constexpr const wchar_t* kDefaultMethod = L"InitializeApp";
constexpr const wchar_t* kTerminateMethod = L"Terminate";
// NativeHost 公开环境变量名；launcher、managed host 与诊断脚本会依赖这些键。
constexpr const wchar_t* kEnvHostAssembly = L"CTK_HOST_ASSEMBLY";
constexpr const wchar_t* kEnvHostMethod = L"CTK_HOST_METHOD";
constexpr const wchar_t* kEnvHostNativeDll = L"CTK_HOST_NATIVE_DLL";
constexpr const wchar_t* kEnvHostLogDir = L"CTK_HOST_LOG_DIR";
constexpr const wchar_t* kEnvBootstrapTraceId = L"CTK_BOOTSTRAP_TRACE_ID";
constexpr const wchar_t* kEnvBootstrapLogRetentionDays = L"CTK_BOOTSTRAP_LOG_RETENTION_DAYS";
constexpr const wchar_t* kEnvHostLogRetentionDays = L"CTK_HOST_LOG_RETENTION_DAYS";
constexpr const wchar_t* kEnvLocalAppData = L"LOCALAPPDATA";
// 新名 env（旧名 CTK_HOST_LOG_DIR 保留兼容别名）；此处仅用于 deprecated 检测，
// 实际解析在 host_log_resolve_dir 内单点完成。
constexpr const wchar_t* kEnvBootstrapLogDir = L"CTK_BOOTSTRAP_LOG_DIR";

// 固定目录/文件名是 native bootstrap 证据链的一部分，避免散落字符串漂移。
constexpr const wchar_t* kManagedDirName = L"managed";
constexpr const wchar_t* kAttachDirName = L"CreoToolkit";
constexpr const wchar_t* kBootstrapLogPrefix = L"native-bootstrap";
constexpr const wchar_t* kSessionIndexPrefix = L"session-index";
constexpr const wchar_t* kJsonlExtension = L".jsonl";
constexpr const wchar_t* kPointerExtension = L".pointer";
constexpr const wchar_t* kAttachLogPrefix = L"ctk-attach-p";
constexpr const wchar_t* kLogExtension = L".log";
constexpr const wchar_t* kLatestSessionPointer = L"latest-session.pointer";
constexpr const wchar_t* kBootstrapJsonlFileFormat = L"%ls\\native-bootstrap-%ls-p%lu-%ls.jsonl";

// OTel-shaped 事件名；外部诊断工具按这些稳定名称过滤。
constexpr const char* kEventModuleResolveFailed = "ctk.native_host.loader.module_resolve_failed";
constexpr const char* kEventManagedAssemblyMismatch = "ctk.native_host.clr.managed_assembly_mismatch";
constexpr const char* kEventEnvDeprecated = "ctk.native_host.config.env_deprecated";
constexpr const char* kEventEntryLoadFailed = "ctk.native_host.clr.entry_load_failed";
constexpr const char* kEventEntryInvoked = "ctk.native_host.clr.entry_invoked";
constexpr const char* kEventEntryReturned = "ctk.native_host.clr.entry_returned";
constexpr const char* kEventDllAttachEntered = "ctk.native_host.lifecycle.dll_attach_entered";
constexpr const char* kEventTraceIdAssigned = "ctk.native_host.handoff.trace_id_assigned";
#ifdef CTK_ENABLE_ETW
constexpr const char* kEventEtwProviderRegistered = "ctk.native_host.lifecycle.etw_provider_registered";
constexpr const char* kEventEtwProviderUnregistered = "ctk.native_host.lifecycle.etw_provider_unregistered";
#endif
constexpr const char* kEventUserInitializeEntered = "ctk.native_host.lifecycle.user_initialize_entered";
constexpr const char* kEventUserInitializeReentered = "ctk.native_host.lifecycle.user_initialize_reentered";
constexpr const char* kEventManagedPayloadResolved = "ctk.native_host.loader.managed_payload_resolved";
constexpr const char* kEventManagedPayloadMissing = "ctk.native_host.loader.managed_payload_missing";
constexpr const char* kEventToolkitTouchFailed = "ctk.native_host.fatal.toolkit_touch_failed";
constexpr const char* kEventUserTerminateEntered = "ctk.native_host.lifecycle.user_terminate_entered";
constexpr const char* kEventUserTerminateReentered = "ctk.native_host.lifecycle.user_terminate_reentered";
constexpr const char* kEventSessionEnd = "ctk.native_host.lifecycle.session_end";

constexpr const char* kSeverityInfo = "INFO";
constexpr const char* kSeverityWarn = "WARN";
constexpr const char* kSeverityError = "ERROR";
constexpr const char* kSeverityFatal = "FATAL";
/* OTel severityNumber 规范值，与上面 severityText 成对使用。 */
constexpr int kSevNumInfo  = 9;
constexpr int kSevNumWarn  = 13;
constexpr int kSevNumError = 17;
constexpr int kSevNumFatal = 21;

/* 诊断信息走 write_otel_event。 */

bool env_present(const wchar_t* name)
{
    wchar_t value[2];
    DWORD n = GetEnvironmentVariableW(name, value, 2);
    return n > 0;
}

bool module_path(wchar_t* out, size_t out_count)
{
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&module_path),
            &module)) {
        write_otel_event_hr(kEventModuleResolveFailed,
                            kSevNumError, kSeverityError,
                            "GetModuleHandleExW(module_path) failed",
                            (int)GetLastError());
        return false;
    }

    DWORD n = GetModuleFileNameW(module, out, static_cast<DWORD>(out_count));
    if (n == 0 || n >= out_count) {
        write_otel_event_hr(kEventModuleResolveFailed,
                            kSevNumError, kSeverityError,
                            "GetModuleFileNameW(module_path) failed",
                            (int)GetLastError());
        return false;
    }
    return true;
}

/* module_path 去掉文件名部分 = 本 DLL 所在目录。 */
bool module_dir(wchar_t* out, size_t out_count)
{
    if (!module_path(out, out_count)) {
        return false;   /* module_path 内已写 module_resolve_failed 事件 */
    }

    wchar_t* slash = wcsrchr(out, L'\\');
    if (slash == nullptr) {
        write_otel_event(kEventModuleResolveFailed,
                         kSevNumError, kSeverityError,
                         "module path has no directory separator");
        return false;
    }

    *slash = L'\0';
    return true;
}

void publish_native_host_path()
{
    wchar_t path[MAX_PATH];
    if (!module_path(path, MAX_PATH)) {
        return;
    }
    SetEnvironmentVariableW(kEnvHostNativeDll, path);

    wchar_t dir[MAX_PATH];
    wcsncpy_s(dir, path, _TRUNCATE);
    wchar_t* slash = wcsrchr(dir, L'\\');
    if (slash != nullptr) {
        *slash = L'\0';
        /* Do not prepend this directory onto process PATH: that changes DLL
         * search order for the whole xtop process. SetDllDirectory is enough
         * for subsequent LoadLibrary/DllImport of this module's directory. */
        SetDllDirectoryW(dir);
    }
}

bool combine_path(wchar_t* out, size_t out_count, const wchar_t* dir, const wchar_t* name)
{
    int written = swprintf_s(out, out_count, L"%ls\\%ls", dir, name);
    return written > 0 && static_cast<size_t>(written) < out_count;
}

/* 存在且非目录（GetFileAttributesW 裸判会把同名目录当命中）。 */
bool file_exists(const wchar_t* path)
{
    DWORD attrs = GetFileAttributesW(path);
    return attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

/* 解析 managed payload 路径：env 显式 > 开发布局 <native>\managed > 发布布局 <native>\..\managed。
 * 三条候选全部做存在性探测；终值写 managed_payload_resolved（含布局来源），
 * 全缺时逐候选写 managed_payload_missing 后失败——错误在最早层曝出，
 * 不再延迟到 entry_load_failed 才可见。 */
bool get_env_path_or_default(
    const wchar_t* env_name,
    const wchar_t* default_file,
    wchar_t* out,
    size_t out_count)
{
    DWORD n = GetEnvironmentVariableW(env_name, out, static_cast<DWORD>(out_count));
    if (n > 0 && n < out_count) {
        /* 用户显式指定的路径不存在 = 配置错误，fail-fast 并留明确证据。 */
        if (!file_exists(out)) {
            write_otel_event_wpath(kEventManagedPayloadMissing,
                                   kSevNumError, kSeverityError,
                                   "env-specified managed payload not found:", out);
            return false;
        }
        write_otel_event_wpath(kEventManagedPayloadResolved,
                               kSevNumInfo, kSeverityInfo,
                               "managed payload resolved (source=env):", out);
        return true;
    }

    wchar_t dir[MAX_PATH];
    if (!module_dir(dir, MAX_PATH)) {
        return false;   /* module_dir 内已写 module_resolve_failed 事件 */
    }

    // 开发输出是 <native>\managed；发布输出是 <deploy>\native + <deploy>\managed。
    // 优先保留开发布局，目标文件不存在时再探测发布布局。
    wchar_t managed_dir[MAX_PATH];
    if (!combine_path(managed_dir, MAX_PATH, dir, kManagedDirName)) {
        write_otel_event(kEventModuleResolveFailed,
                         kSevNumError, kSeverityError, "managed directory path is too long");
        return false;
    }

    wchar_t development_path[MAX_PATH];
    if (!combine_path(development_path, MAX_PATH, managed_dir, default_file)) {
        write_otel_event(kEventModuleResolveFailed,
                         kSevNumError, kSeverityError, "managed file path is too long");
        return false;
    }
    if (file_exists(development_path)) {
        wcscpy_s(out, out_count, development_path);
        write_otel_event_wpath(kEventManagedPayloadResolved,
                               kSevNumInfo, kSeverityInfo,
                               "managed payload resolved (source=dev-layout):", out);
        return true;
    }

    wchar_t packaged_raw[MAX_PATH];
    if (swprintf_s(packaged_raw, L"%ls\\..\\%ls\\%ls", dir, kManagedDirName, default_file) <= 0) {
        write_otel_event(kEventModuleResolveFailed,
                         kSevNumError, kSeverityError, "packaged managed file path is too long");
        return false;
    }
    /* 规范化 ..：事件拿干净路径；规范化失败退回原始拼接（行为不回退）。 */
    wchar_t packaged_path[MAX_PATH];
    DWORD full_n = GetFullPathNameW(packaged_raw, MAX_PATH, packaged_path, nullptr);
    if (full_n == 0 || full_n >= MAX_PATH) {
        wcscpy_s(packaged_path, MAX_PATH, packaged_raw);
    }
    if (file_exists(packaged_path)) {
        wcscpy_s(out, out_count, packaged_path);
        write_otel_event_wpath(kEventManagedPayloadResolved,
                               kSevNumInfo, kSeverityInfo,
                               "managed payload resolved (source=packaged-layout):", out);
        return true;
    }

    /* 双布局皆缺：逐候选各写一条（含完整路径），invoke_managed 兜底 rc -10。 */
    write_otel_event_wpath(kEventManagedPayloadMissing,
                           kSevNumError, kSeverityError,
                           "managed payload not found in dev layout:", development_path);
    write_otel_event_wpath(kEventManagedPayloadMissing,
                           kSevNumError, kSeverityError,
                           "managed payload not found in packaged layout:", packaged_path);
    return false;
}

int invoke_managed(const wchar_t* method)
{
    wchar_t assembly_path[MAX_PATH];
    if (cached_managed_assembly[0] != L'\0') {
        wcscpy_s(assembly_path, cached_managed_assembly);
    } else if (!get_env_path_or_default(
            kEnvHostAssembly,
            kManagedAssembly,
            assembly_path,
            MAX_PATH)) {
        write_otel_event(kEventModuleResolveFailed,
                         kSevNumError, kSeverityError,
                         "assembly path resolve failed (rc -10)");
        return -10;
    }

    if (cached_managed_assembly[0] != L'\0' &&
        _wcsicmp(assembly_path, cached_managed_assembly) != 0) {
        write_otel_event_wpath(kEventManagedAssemblyMismatch,
                               kSevNumWarn, kSeverityWarn,
                               "managed assembly differs from the first resolved path; reusing pinned assembly:",
                               cached_managed_assembly);
        wcscpy_s(assembly_path, cached_managed_assembly);
    }

    write_otel_event_wpath(kEventEntryInvoked,
                           kSevNumInfo, kSeverityInfo, "invoking managed entry:", method);
    ctk::clr_host::InvokeResult r = ctk::clr_host::execute(
        assembly_path, kTypeName, method, L"");
    if (!r.executed) {
        write_otel_event_hr(kEventEntryLoadFailed,
                            kSevNumError, kSeverityError,
                            "ExecuteInDefaultAppDomain failed", (int)r.hr);
        return -12;
    }
    if (cached_managed_assembly[0] == L'\0') {
        wcscpy_s(cached_managed_assembly, assembly_path);
    }
    int rc = (int)r.ret_code;
    write_otel_event_hr(kEventEntryReturned,
                        rc == 0 ? kSevNumInfo : kSevNumError,
                        rc == 0 ? kSeverityInfo : kSeverityError,
                        "managed entry returned", rc);
    return rc;
}

const wchar_t* initialize_method()
{
    static wchar_t method[128];
    DWORD n = GetEnvironmentVariableW(kEnvHostMethod, method, 128);
    if (n == 0) {
        return kDefaultMethod;
    }
    if (n < 128 &&
        (wcscmp(method, L"Initialize") == 0 ||
         wcscmp(method, L"InitializeAndAttach") == 0 ||
         wcscmp(method, L"InitializeApp") == 0)) {
        return method;
    }
    write_otel_event(kEventEntryLoadFailed,
                     kSevNumError, kSeverityError,
                     "CTK_HOST_METHOD must be Initialize, InitializeAndAttach, or InitializeApp");
    return nullptr;
}

/* ====================================================================
 * OTel-shaped bootstrap evidence 函数实现
 * ==================================================================== */

/* 用 BCryptGenRandom 生成 128-bit W3C trace_id（仅 user_initialize 安全点调用，禁 DllMain）。
 * 全零非法（W3C 规约），命中时把第 0 字节设为 1 兜底（概率 1/2^128，几乎不可能）。
 * out_count >= 33。 */
int generate_trace_id(wchar_t* out, size_t out_count)
{
    if (out == nullptr || out_count < 33) return 1;

    BYTE buf[16];
    NTSTATUS rc = BCryptGenRandom(nullptr, buf, sizeof(buf),
                                   BCRYPT_USE_SYSTEM_PREFERRED_RNG);
    if (rc != 0) return 2;

    /* W3C: 全零 trace_id 非法 */
    bool all_zero = true;
    for (int i = 0; i < 16; ++i) {
        if (buf[i] != 0) { all_zero = false; break; }
    }
    if (all_zero) buf[0] = 1;

    static const wchar_t kHex[] = L"0123456789abcdef";
    for (int i = 0; i < 16; ++i) {
        out[i * 2]     = kHex[(buf[i] >> 4) & 0xf];
        out[i * 2 + 1] = kHex[buf[i] & 0xf];
    }
    out[32] = L'\0';
    return 0;
}

/* 拼装 bootstrap JSONL 文件路径：
 *   {g_host_log_dir}\native-bootstrap-{iso_stamp}-p{pid}-{trace_id}.jsonl
 * 未来可扩展到 diagnostics/native-bootstrap/<plugin>/<version>/... 布局。 */
int build_bootstrap_jsonl_path(wchar_t* out, size_t out_count)
{
    if (g_host_log_dir[0] == L'\0' || g_trace_id[0] == L'\0') return 1;

    wchar_t iso[24];
    if (host_log_make_iso_stamp(iso, 24) != 0) return 2;

    int n = swprintf_s(out, out_count, kBootstrapJsonlFileFormat,
                        g_host_log_dir, iso, (unsigned long)g_host_pid, g_trace_id);
    return (n > 0 && (size_t)n < out_count) ? 0 : 3;
}

/* json_escape_a 已提出到 host_log_paths（host_log_json_escape_a），
 * 此处通过 host_log_paths.h 调用，不再重复定义。 */

/* 写一条 OTel-shaped JSONL 事件到 g_bootstrap_jsonl。
 * 薄壳：取全局状态（时间/seq/trace_id）→ 调格式函数 → 追加文件。
 * 格式化逻辑已提取到 host_log_format_otel_event（host_log_paths.h），
 * 使测试可直接验证 JSON 模板而无需链入 host_entry 全局状态。 */
int write_otel_event(const char* event_name,
                     int severity_number,
                     const char* severity_text,
                     const char* body_utf8 /* nullable */)
{
    if (g_bootstrap_jsonl[0] == L'\0') return 1;
    if (event_name == nullptr || severity_text == nullptr) return 1;

    /* timeUnixNano: FILETIME(UTC 1601-01-01) → unix epoch nanoseconds */
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    ULARGE_INTEGER u;
    u.LowPart  = ft.dwLowDateTime;
    u.HighPart = ft.dwHighDateTime;
    ULONGLONG unix_100ns = u.QuadPart - 116444736000000000ULL;
    ULONGLONG unix_nano  = unix_100ns * 100ULL;

    LONG seq = InterlockedIncrement(&g_event_seq);

    /* trace_id / bootstrap_id: wchar ASCII hex → char 安全 */
    char trace_id_a[33];
    char bootstrap_id_a[33];
    for (int i = 0; i < 32; ++i) {
        trace_id_a[i]     = (char)g_trace_id[i];
        bootstrap_id_a[i] = (char)g_bootstrap_id[i];
    }
    trace_id_a[32]     = '\0';
    bootstrap_id_a[32] = '\0';

    const char* resource = g_resource_json[0] != '\0' ? g_resource_json : "{}";

    /* 4096 = 固定字段 ~400 + resource ≤256 + 转义 body ≤2048 + 余量（与扩容后 body 链配套）。 */
    char json[4096];
    int rc = host_log_format_otel_event(
        event_name, severity_number, severity_text, body_utf8,
        trace_id_a, bootstrap_id_a, resource, (long)seq,
        unix_nano, json, sizeof(json));
    if (rc != 0) return 2;

    int io_rc = host_log_append_jsonl_event(g_bootstrap_jsonl, json);

#ifdef CTK_ENABLE_ETW
    ctk_etw_write_event(event_name, severity_number, severity_text,
                        trace_id_a, body_utf8);
#endif

    return io_rc;
}

/* 从绝对路径中抽取 basename（最后一个 \ 或 / 之后的部分）。
 * 用于 pointer 文件 content + session-index record fileName 字段。 */
void path_basename(const wchar_t* full, wchar_t* out, size_t out_cap)
{
    const wchar_t* last = full;
    for (const wchar_t* p = full; *p != L'\0'; ++p) {
        if (*p == L'\\' || *p == L'/') last = p + 1;
    }
    size_t i = 0;
    while (last[i] != L'\0' && i + 1 < out_cap) {
        out[i] = last[i];
        ++i;
    }
    out[i] = L'\0';
}

/* 把 wchar_t basename（ASCII subset）转 char UTF-8。仅用于 pointer/index content。
 * 文件名严格 ASCII（native-bootstrap-{iso}-p{pid}-{traceId32}.jsonl），cast 安全。 */
void wstr_to_ascii(const wchar_t* in, char* out, size_t out_cap)
{
    size_t i = 0;
    while (in[i] != L'\0' && i + 1 < out_cap) {
        out[i] = (char)in[i];
        ++i;
    }
    out[i] = '\0';
}

/* DiagnosticBundle locates the current JSONL via latest-session.pointer. */
void write_latest_session_pointer()
{
    if (g_host_log_dir[0] == L'\0' || g_bootstrap_jsonl[0] == L'\0') return;

    wchar_t base_w[MAX_PATH];
    path_basename(g_bootstrap_jsonl, base_w, MAX_PATH);
    char base_a[MAX_PATH];
    wstr_to_ascii(base_w, base_a, MAX_PATH);
    host_log_write_pointer(g_host_log_dir, kLatestSessionPointer, base_a);
}

/* Creo 对 user_initialize 非零通常不再回调 user_terminate；native 资源必须在这里自清理。 */
int finish_user_initialize(int rc)
{
    if (rc == 0) return 0;

    if (g_bootstrap_jsonl[0] != L'\0') {
        write_otel_event_hr(kEventSessionEnd,
                            kSevNumError, kSeverityError,
                            "user_initialize failed; native bootstrap cleanup", rc);
    }
#ifdef CTK_ENABLE_ETW
    ctk_etw_shutdown();
    if (g_bootstrap_jsonl[0] != L'\0') {
        write_otel_event(kEventEtwProviderUnregistered,
                         kSevNumInfo, kSeverityInfo,
                         "ETW provider unregistered (init failure cleanup)");
    }
#endif
    return rc;
}

/* 初始化 trace context（仅 user_initialize 安全点调用）。
 * 生成 traceId → 算 JSONL 路径 → 写 pointer/session-index → 写 dll_attach_entered + trace_id_assigned binding 事件。
 * 返回 0 成功；非 0 时相关事件全部 noop（不影响 CLR 加载流程）。
 * 幂等：多 DLL 实例场景（双 protk 注册）下若被重入，已初始化则直接返回，
 *      避免覆盖 g_trace_id 让前一实例的 ack 失配。 */
int init_trace_context()
{
    if (g_trace_id[0] != L'\0') return 0;

    if (generate_trace_id(g_trace_id, 33) != 0) {
        g_trace_id[0] = L'\0';
        return 1;
    }
    if (build_bootstrap_jsonl_path(g_bootstrap_jsonl, MAX_PATH) != 0) {
        g_bootstrap_jsonl[0] = L'\0';
        return 2;
    }
    write_latest_session_pointer();

    /* 历史回放：DllMain 内已发生的 DLL_PROCESS_ATTACH 事件，现在写入 */
    write_otel_event(kEventDllAttachEntered,
                     kSevNumInfo, kSeverityInfo,
                     "DLL_PROCESS_ATTACH already occurred in DllMain; logged retroactively");
    /* 绑定事件：bootstrapId ↔ traceId */
    write_otel_event(kEventTraceIdAssigned,
                     kSevNumInfo, kSeverityInfo,
                     "W3C trace_id generated; bootstrapId bound in attributes");
    /* managed Bootstrap 用此值调用 ctk_native_log_handoff_ack；它是进程内 handoff 状态，不是用户配置。 */
    SetEnvironmentVariableW(kEnvBootstrapTraceId, g_trace_id);
    return 0;
}

/* ====================================================================
 * DllMain attach 证据（loader lock 安全）
 *
 * 为什么这里只做这一件事：DLL_PROCESS_ATTACH 在 OS loader lock 持锁下执行，
 * 可安全调用的 API 极窄（仅 Kernel32 白名单），严禁分配堆 / 建线程 / LoadLibrary /
 * 递归建目录 / 持久 handle。历史上出现过"DLL attach 了但 user_initialize 没来"的
 * 故障，故 attach 证据必须落在 DllMain 内；但要把 IO 压到最小：免路径解析、单次写、
 * 写完即关。复杂目录解析 / 归档 / retention 全部下移到 user_initialize 安全点。
 * ==================================================================== */

/* 追加宽串，防越界，维护 pos。 */
void dm_wappend(wchar_t* buf, size_t cap, size_t& pos, const wchar_t* s)
{
    while (*s != L'\0' && pos + 1 < cap) buf[pos++] = *s++;
    buf[pos] = L'\0';
}

/* 追加 DWORD 十进制，防越界。 */
void dm_wappend_u32(wchar_t* buf, size_t cap, size_t& pos, DWORD v)
{
    wchar_t tmp[16]; int i = 0;
    if (v == 0) tmp[i++] = L'0';
    while (v > 0) { tmp[i++] = (wchar_t)(L'0' + (v % 10)); v /= 10; }
    while (i > 0 && pos + 1 < cap) buf[pos++] = tmp[--i];
    buf[pos] = L'\0';
}

/* 追加窄串（ASCII），防越界，维护 pos。 */
void dm_aappend(char* buf, size_t cap, size_t& pos, const char* s)
{
    while (*s != '\0' && pos + 1 < cap) buf[pos++] = *s++;
    buf[pos] = '\0';
}

/* u64 十进制写入 out（含 NUL），返回字符数。 */
size_t dm_u64_ascii(unsigned long long v, char* out)
{
    char tmp[24]; int i = 0;
    if (v == 0) tmp[i++] = '0';
    while (v > 0) { tmp[i++] = (char)('0' + (v % 10)); v /= 10; }
    int j = 0;
    while (i > 0) out[j++] = tmp[--i];
    out[j] = '\0';
    return (size_t)j;
}

/* attach 证据的 base 目录：%LOCALAPPDATA% 优先，失败退 TEMP（去尾斜杠）。
 * 纯 Kernel32，DllMain 安全。DllMain 写证据与 user_initialize retention 清理
 * 共用，防两处目录构造漂移。返回 false = 无处可写。 */
bool resolve_attach_base_dir(wchar_t* dir, size_t cap)
{
    DWORD n = GetEnvironmentVariableW(kEnvLocalAppData, dir, (DWORD)cap);
    if (n > 0 && n < cap) return true;

    n = GetTempPathW((DWORD)cap, dir);             /* Kernel32；末尾带反斜杠 */
    if (n == 0 || n >= cap) return false;
    if (dir[n - 1] == L'\\' || dir[n - 1] == L'/') dir[n - 1] = L'\0';
    return true;
}

/* DllMain 内唯一的诊断落地：写一行 OTel-shaped dll_attach_entered JSONL。
 * 纯 Kernel32 + 栈缓冲，无 CRT 分配、无 BCrypt、无 user32(wsprintf)。
 * 此刻 traceId 尚未生成（CSPRNG 禁于 DllMain，且全零 traceId 非法），故本行不含
 * traceId 字段；关联键用 bootstrapId，user_initialize 安全点再回放并绑定 traceId。 */
void dllmain_write_attach_evidence()
{
    DWORD pid = GetCurrentProcessId();

    /* 目录免解析（不做可写性探针）；解析失败 = 无处可写，放弃（不阻断 attach）。 */
    wchar_t dir[MAX_PATH];
    if (!resolve_attach_base_dir(dir, MAX_PATH)) return;

    /* 目标 = dir\CreoToolkit\ctk-attach-p{pid}.log；最多一次 CreateDirectoryW。 */
    wchar_t path[MAX_PATH];
    size_t pos = 0;
    dm_wappend(path, MAX_PATH, pos, dir);
    dm_wappend(path, MAX_PATH, pos, L"\\");
    dm_wappend(path, MAX_PATH, pos, kAttachDirName);
    CreateDirectoryW(path, nullptr);               /* 已存在/无权限都继续 */
    dm_wappend(path, MAX_PATH, pos, L"\\");
    dm_wappend(path, MAX_PATH, pos, kAttachLogPrefix);
    dm_wappend_u32(path, MAX_PATH, pos, pid);
    dm_wappend(path, MAX_PATH, pos, kLogExtension);

    /* 时间：GetSystemTimeAsFileTime（Kernel32）→ unix 纳秒。 */
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    ULARGE_INTEGER u; u.LowPart = ft.dwLowDateTime; u.HighPart = ft.dwHighDateTime;
    unsigned long long unix_nano = (u.QuadPart - 116444736000000000ULL) * 100ULL;

    /* 手工拼一行 JSONL（全 ASCII，无 CRT 格式化）。 */
    char line[512];
    size_t p = 0;
    dm_aappend(line, sizeof(line), p, "{\"timeUnixNano\":\"");
    p += dm_u64_ascii(unix_nano, line + p);
    dm_aappend(line, sizeof(line), p, "\",\"severityText\":\"");
    dm_aappend(line, sizeof(line), p, kSeverityInfo);
    dm_aappend(line, sizeof(line), p,
               "\",\"severityNumber\":9,"
               "\"eventName\":\"");
    dm_aappend(line, sizeof(line), p, kEventDllAttachEntered);
    dm_aappend(line, sizeof(line), p,
               "\","
               "\"body\":\"DLL_PROCESS_ATTACH entered under loader lock\","
               "\"attributes\":{\"bootstrapId\":\"");
    for (int i = 0; i < 32 && g_bootstrap_id[i] != L'\0'; ++i)
        if (p + 1 < sizeof(line)) line[p++] = (char)g_bootstrap_id[i];  /* ASCII hex，宽转窄安全 */
    dm_aappend(line, sizeof(line), p, "\",\"pid\":");
    p += dm_u64_ascii((unsigned long long)pid, line + p);
    dm_aappend(line, sizeof(line), p, "}}\n");

    /* 单次 CreateFileW + WriteFile + CloseHandle，一进程一文件覆写，写完即关。 */
    HANDLE h = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr,
                           CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    DWORD written = 0;
    WriteFile(h, line, (DWORD)p, &written, nullptr);
    CloseHandle(h);
}

} // namespace

// DllMain 只做 loader lock 安全的最小动作：记 hinst + 生成 bootstrapId + 落一条 attach 证据。
// 路径解析（含递归建目录）/归档/retention/trace context 全部下移到 user_initialize 安全点。
extern "C" BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) {
        /* 记住 hinst 供 user_initialize 解析 session 路径（此处不解析，避免 loader lock 下超标 IO）。 */
        g_hinst_dll = hinst;

        /* 生成 bootstrapId（纯 Kernel32 + FNV-1a，DllMain 安全）。 */
        host_log_make_bootstrap_id(hinst, g_bootstrap_id, 33);

        /* 唯一 attach 证据：免解析路径 + 单次写 + 写完即关；保住"attach 到 user_initialize"
         * 之间的证据窗口（历史上 user_initialize 可能不被调用）。traceId 由 user_initialize 回放绑定。 */
        dllmain_write_attach_evidence();
    } else if (reason == DLL_PROCESS_DETACH) {
        /* detach 同在 loader lock 下，不做额外 IO。 */
    }
    return TRUE;
}

extern "C" __declspec(dllexport) int user_initialize(
    int /*argc*/,
    char* /*argv*/[],
    char* proe_vsn,
    char* /*build*/)
{
    /* 早期顺序：
     * 0) init_session_paths（含递归建目录，DllMain 内被禁，故下移到此）
     * 1) retention cleanup（删 > N 天的过期诊断文件）
     * 2) SetEnvironmentVariableW 回写 log dir
     * 3) trace context 初始化
     * 4) 继续 toolkit_touch / Bootstrap */

    /* 重入守卫（双 protk.dat 条目等错配）：全局状态是进程级单份，不论成败只许完整跑一次。
     * 成功后重入：若走完整流程，managed 侧幂等 REJECT 返回非零 → finish_user_initialize
     *   会注销活跃 ETW provider、删 active pointer、写假 fatal 指针——摧毁第一实例的
     *   三轨证据。no-op 返 0，避免 Creo 对同一 DLL 做失败处置。
     * 失败后重入：同进程重试不可能自愈（失败模式全部是环境/配置性的），诚实拒绝返非零，
     *   不重跑、不做任何状态恢复。 */
    {
        LONG init_state = g_user_init_state;
        if (init_state != 0) {
            const bool succeeded = (init_state == 2);
            write_otel_event(kEventUserInitializeReentered,
                             kSevNumWarn, kSeverityWarn,
                             succeeded
                                 ? "user_initialize re-entered after successful init; duplicate protk.dat entry? no-op"
                                 : "user_initialize re-entered after failed init; in-process retry cannot succeed; rejected");
            return succeeded ? 0 : -14;
        }
        InterlockedExchange(&g_user_init_state, 1);
    }

    /* Sample legacy aliases before any env rewrite. */
    bool legacy_dir_env_hit = !env_present(kEnvBootstrapLogDir) && env_present(kEnvHostLogDir);
    bool legacy_retention_env_hit = !env_present(kEnvBootstrapLogRetentionDays) &&
                                    env_present(kEnvHostLogRetentionDays);

    /* 路径解析（含递归建目录）从 DllMain 下移到此安全点执行——loader lock 已释放，
     * 可安全做目录解析/创建。此后 g_host_log_dir 就绪。 */
    init_session_paths(g_hinst_dll);

    /* retention：新名 CTK_BOOTSTRAP_LOG_RETENTION_DAYS 优先，回退旧名 CTK_HOST_LOG_RETENTION_DAYS
     * （保留兼容别名）。允许 0/负数 disable（host_log_cleanup_* 内 <=0 跳过）。
     * 用哨兵值区分「新名未设置」与「新名设为 0/负数」——0/负数是合法的 disable 语义，不该回退。 */
    const int kRetentionEnvAbsent = 0x7FFFFFFF;
    int retention_days = parse_int_env_allow_zero_or_negative(
        kEnvBootstrapLogRetentionDays, kRetentionEnvAbsent);
    if (retention_days == kRetentionEnvAbsent) {
        retention_days = parse_int_env_allow_zero_or_negative(
            kEnvHostLogRetentionDays, 14);
    }

    /* Retention before this session's JSONL/pointer exist, so cleanup cannot
     * delete the files we are about to write. session-index.jsonl cleanup
     * remains so leftover ledgers from older builds age out. */
    host_log_cleanup_dir_by_mtime(g_host_log_dir, kBootstrapLogPrefix, kJsonlExtension, retention_days);
    host_log_cleanup_dir_by_mtime(g_host_log_dir, kSessionIndexPrefix, kJsonlExtension, retention_days);
    host_log_cleanup_dir_by_mtime(g_host_log_dir, nullptr,             kPointerExtension, retention_days);

    /* DllMain attach 证据 ctk-attach-p*.log 落在 %LOCALAPPDATA%\CreoToolkit（非 logs\host），
     * 需单独按同阈值清理；目录构造经 resolve_attach_base_dir 与 dllmain_write_attach_evidence 共用。 */
    {
        wchar_t attach_dir[MAX_PATH];
        if (resolve_attach_base_dir(attach_dir, MAX_PATH)) {
            wchar_t attach_full[MAX_PATH];
            if (swprintf_s(attach_full, MAX_PATH, L"%ls\\%ls", attach_dir, kAttachDirName) > 0) {
                host_log_cleanup_dir_by_mtime(attach_full, kAttachLogPrefix, kLogExtension,
                                              retention_days);
            }
        }
    }

    /* Publish the resolved directory under the current name only. Legacy
     * CTK_HOST_LOG_DIR is still *read* by host_log_resolve_dir; rewriting it
     * here would keep the deprecated alias alive for every child. */
    SetEnvironmentVariableW(kEnvBootstrapLogDir, g_host_log_dir);

    /* JSONL bootstrap evidence (optional ETW twin when CTK_ENABLE_ETW).
     * Failures here must not block CLR load. */
    int trace_rc = init_trace_context();
    if (trace_rc == 0) {
        /* resource JSON 片段：OTel semconv 裸名 + ctk. 前缀自定义 */
        char vsn_escaped[128];
        const char* vsn_raw = (proe_vsn != nullptr && proe_vsn[0] != '\0') ? proe_vsn : "unknown";
        if (host_log_json_escape_a(vsn_raw, vsn_escaped, sizeof(vsn_escaped)) != 0)
            vsn_escaped[0] = '\0';
        sprintf_s(g_resource_json, sizeof(g_resource_json),
            "{\"service.name\":\"creo-toolkit-native-host\",\"process.pid\":%lu,\"ctk.creo.version\":\"%s\"}",
            (unsigned long)g_host_pid, vsn_escaped);
#ifdef CTK_ENABLE_ETW
        int etw_rc = ctk_etw_init();
        if (etw_rc == 0) {
            write_otel_event(kEventEtwProviderRegistered,
                             kSevNumInfo, kSeverityInfo, "ETW provider registered");
        }
#endif
        write_otel_event(kEventUserInitializeEntered,
                         kSevNumInfo, kSeverityInfo, "user_initialize invoked by Creo");
        /* 旧 env 别名命中留证据，提示迁移（与 managed 侧 config.env.deprecated 范式对齐）。 */
        if (legacy_dir_env_hit) {
            write_otel_event(kEventEnvDeprecated, kSevNumWarn, kSeverityWarn,
                             "CTK_HOST_LOG_DIR is deprecated; use CTK_BOOTSTRAP_LOG_DIR");
        }
        if (legacy_retention_env_hit) {
            write_otel_event(kEventEnvDeprecated, kSevNumWarn, kSeverityWarn,
                             "CTK_HOST_LOG_RETENTION_DAYS is deprecated; use CTK_BOOTSTRAP_LOG_RETENTION_DAYS");
        }
    }
    /* =============================================== */

    // 满足链接期 ucore 引用 + 运行期"至少调一次 TOOLKIT API"双重要求
    int touch_rc = ctk_toolkit_touch();
    if (touch_rc != 0) {
        if (g_bootstrap_jsonl[0] != L'\0') {
            write_otel_event(kEventToolkitTouchFailed,
                             kSevNumFatal, kSeverityFatal, "Pro_TK ctk_toolkit_touch returned non-zero");
        }
        return finish_user_initialize(touch_rc);
    }
    publish_native_host_path();
    const wchar_t* method = initialize_method();
    if (method == nullptr) {
        return finish_user_initialize(-13);
    }
    int rc = invoke_managed(method);
    if (rc == 0) {
        InterlockedExchange(&g_user_init_state, 2);   /* 重入守卫据此 no-op 直退 */
    }

    /* entry 调用/返回事件由 invoke_managed 内 clr.entry_invoked / entry_returned
     * 承接（白名单正名，替代旧 managed_entry_invoked）；统一失败收尾在 finish_user_initialize。 */
    return finish_user_initialize(rc);
}

extern "C" __declspec(dllexport) void user_terminate(void)
{
    /* 对称重入守卫：双 protk.dat 条目下 Creo 可能按条目调两次 terminate；
     * 清理只许跑一次，重复调用仅留 WARN 证据（JSONL 轨；ETW 已注销属预期）。 */
    if (InterlockedExchange(&g_user_terminate_entered, 1) != 0) {
        write_otel_event(kEventUserTerminateReentered,
                         kSevNumWarn, kSeverityWarn,
                         "user_terminate re-entered; duplicate protk.dat entry? no-op");
        return;
    }

    /* 进入 terminate 阶段事件 */
    if (g_bootstrap_jsonl[0] != L'\0') {
        write_otel_event(kEventUserTerminateEntered,
                         kSevNumInfo, kSeverityInfo, "user_terminate invoked by Creo");
    }

    /* CLR 守卫：从未 Start = 跳过 managed Terminate，避免退出路径冷启动 CLR。 */
    const bool clr_started = ctk::clr_host::is_started();
    int managed_rc = clr_started ? invoke_managed(kTerminateMethod) : 0;

    if (g_bootstrap_jsonl[0] != L'\0') {
        int sev_num = kSevNumInfo;
        const char* sev_text = kSeverityInfo;
        const char* body = "graceful terminate";
        if (!clr_started) {
            sev_num = kSevNumWarn;
            sev_text = kSeverityWarn;
            body = "managed terminate skipped; CLR was never started";
        } else if (managed_rc != 0) {
            sev_num = kSevNumError;
            sev_text = kSeverityError;
            body = "managed terminate failed; native cleanup continued";
        }
        write_otel_event_hr(kEventSessionEnd, sev_num, sev_text, body, managed_rc);
    }

#ifdef CTK_ENABLE_ETW
    ctk_etw_shutdown();
    if (g_bootstrap_jsonl[0] != L'\0') {
        write_otel_event(kEventEtwProviderUnregistered,
                         kSevNumInfo, kSeverityInfo, "ETW provider unregistered");
    }
#endif
}

/* ABI stub: older managed Host.dll may still P/Invoke this. Current Host
 * does not call it. Keep the signature and return codes; do not write events. */
extern "C" __declspec(dllexport) int ctk_native_log_handoff_ack(const char* trace_id_utf8,
                                                                  int len)
{
    if (trace_id_utf8 == nullptr) return -1;
    if (len != 32) return -1;

    for (int i = 0; i < 32; ++i) {
        char c = trace_id_utf8[i];
        bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
        if (!ok) return -1;
    }

    bool all_zero = true;
    for (int i = 0; i < 32; ++i) {
        if (trace_id_utf8[i] != '0') { all_zero = false; break; }
    }
    if (all_zero) return -1;

    char our[32];
    for (int i = 0; i < 32; ++i) our[i] = (char)g_trace_id[i];

    if (memcmp(trace_id_utf8, our, 32) != 0) {
        return -2;
    }

    InterlockedExchange(&g_handoff_acked, 1);
    return 0;
}
