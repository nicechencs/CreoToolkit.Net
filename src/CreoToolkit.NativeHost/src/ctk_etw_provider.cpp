/*
 * ctk_etw_provider.cpp - NativeHost ETW TraceLogging provider 实现（轨道 B）。
 *
 * provider 定义 + register/unregister + 事件发射（ctk_etw_write_event）。
 * ctk_etw_write_event 是 JSONL 轨道 A 的单点 ETW 同源写出，由 write_otel_event 调用。
 */
#include "ctk_etw_provider.h"

#include <cstdint>

/*
 * Provider 定义（编译期固定 GUID，跨编译器跨构建稳定）。
 *
 *   name = "com.creotoolkit.Creo.NativeHost"
 *   GUID = {6181F760-2290-4F7B-98A1-825CD0E1468B}
 *
 * 故意不使用 TraceLogging 从 name 派生 GUID 的默认行为：派生算法跨头文件版本
 * 理论稳定但工具链层 reader（PerfView / tracelog / wpr）订阅必须知道确切 GUID，
 * 显式写死可减少订阅端踩坑。
 */
TRACELOGGING_DEFINE_PROVIDER(
    g_ctk_native_host_provider,
    "com.creotoolkit.Creo.NativeHost",
    // {6181F760-2290-4F7B-98A1-825CD0E1468B}
    (0x6181F760, 0x2290, 0x4F7B, 0x98, 0xA1, 0x82, 0x5C, 0xD0, 0xE1, 0x46, 0x8B));

namespace {

/* 注册状态标志位，保证 shutdown 幂等。仅本翻译单元可见。 */
bool g_ctk_etw_registered = false;

} // namespace

extern "C" int ctk_etw_init(void)
{
    if (g_ctk_etw_registered)
    {
        /* 已注册视为成功，避免重复 Register 触发未定义行为。 */
        return 0;
    }

    HRESULT hr = TraceLoggingRegister(g_ctk_native_host_provider);
    if (FAILED(hr))
    {
        /* HRESULT 是 LONG（int32），负值即失败；直接转 int 返出供调用方记日志。 */
        return static_cast<int>(hr);
    }

    g_ctk_etw_registered = true;
    return 0;
}

extern "C" void ctk_etw_shutdown(void)
{
    if (!g_ctk_etw_registered)
    {
        /* 未注册或已注销，幂等 noop。 */
        return;
    }

    TraceLoggingUnregister(g_ctk_native_host_provider);
    g_ctk_etw_registered = false;
}

/* 前缀长度匹配：a 以 prefix 开头（ASCII 字节比较）。 */
static bool starts_with_a(const char* a, const char* prefix)
{
    if (a == nullptr || prefix == nullptr) return false;
    while (*prefix != '\0') {
        if (*a != *prefix) return false;
        ++a; ++prefix;
    }
    return true;
}

extern "C" unsigned long long ctk_etw_keyword_for_event(const char* event_name)
{
    if (event_name == nullptr) return CTK_ETW_KW_BOOTSTRAP;

    /* 按 eventName 第三段命名空间匹配 keyword。
     * 所有 eventName 形如 "ctk.native_host.<ns>.<action>"。 */
    static const char kPrefix[] = "ctk.native_host.";
    if (!starts_with_a(event_name, kPrefix))
        return CTK_ETW_KW_BOOTSTRAP;

    const char* ns = event_name + (sizeof(kPrefix) - 1);

    if (starts_with_a(ns, "lifecycle.")) return CTK_ETW_KW_LIFECYCLE;
    if (starts_with_a(ns, "loader."))    return CTK_ETW_KW_LOADER;
    if (starts_with_a(ns, "clr."))       return CTK_ETW_KW_CLR;
    if (starts_with_a(ns, "handoff."))   return CTK_ETW_KW_HANDOFF;
    if (starts_with_a(ns, "fatal."))     return CTK_ETW_KW_FATAL;
    if (starts_with_a(ns, "audit."))     return CTK_ETW_KW_AUDIT;

    return CTK_ETW_KW_BOOTSTRAP;
}

namespace {

/* OTel severityNumber → WINEVENT_LEVEL 数值映射。
 * CRITICAL=1 / ERROR=2 / WARNING=3 / INFO=4 / VERBOSE=5。 */
int severity_to_winevent_level(int severity_number)
{
    if (severity_number >= 21) return 1;  /* FATAL → CRITICAL */
    if (severity_number >= 17) return 2;  /* ERROR */
    if (severity_number >= 13) return 3;  /* WARN → WARNING */
    if (severity_number >= 9)  return 4;  /* INFO */
    return 5;                             /* TRACE/DEBUG → VERBOSE */
}

} // namespace

/* TraceLoggingLevel / TraceLoggingKeyword 要求编译期常量，
 * 用宏展开 7 keyword × 5 level 组合，会话级 -matchanykeyword / -level
 * 过滤在 ETW 内核层生效（keyword 精确过滤）。 */
#define CTK_ETW_EMIT(kw_const, lvl_const)                                        \
    TraceLoggingWrite(                                                            \
        g_ctk_native_host_provider,                                              \
        "CtkNativeHostEvent",                                                     \
        TraceLoggingLevel(lvl_const),                                             \
        TraceLoggingKeyword(kw_const),                                            \
        TraceLoggingString(event_name, "eventName"),                              \
        TraceLoggingString(severity_text != nullptr ? severity_text : "",         \
                           "severityText"),                                       \
        TraceLoggingInt32(severity_number, "severityNumber"),                     \
        TraceLoggingString(trace_id != nullptr ? trace_id : "", "traceId"),       \
        TraceLoggingString(body_utf8 != nullptr ? body_utf8 : "", "body"),        \
        TraceLoggingUInt32(GetCurrentProcessId(), "pid"))

#define CTK_ETW_EMIT_LEVELS(kw_const)                                            \
    switch (level) {                                                              \
    case 1:  CTK_ETW_EMIT(kw_const, 1); break;                                    \
    case 2:  CTK_ETW_EMIT(kw_const, 2); break;                                    \
    case 3:  CTK_ETW_EMIT(kw_const, 3); break;                                    \
    case 4:  CTK_ETW_EMIT(kw_const, 4); break;                                    \
    default: CTK_ETW_EMIT(kw_const, 5); break;                                    \
    }

extern "C" void ctk_etw_write_event(const char* event_name,
                                     int severity_number,
                                     const char* severity_text,
                                     const char* trace_id,
                                     const char* body_utf8)
{
    /* provider 未注册时 TraceLoggingWrite 自动 noop，无需前置检查。
     * 但显式检查可避免参数准备开销。 */
    if (!g_ctk_etw_registered) return;
    if (event_name == nullptr) return;

    unsigned long long kw = ctk_etw_keyword_for_event(event_name);
    int level = severity_to_winevent_level(severity_number);

    /* 双层分发：keyword 7 case × level 5 case，全部编译期常量实例化。 */
    switch (kw) {
    case CTK_ETW_KW_LIFECYCLE: CTK_ETW_EMIT_LEVELS(CTK_ETW_KW_LIFECYCLE); break;
    case CTK_ETW_KW_LOADER:    CTK_ETW_EMIT_LEVELS(CTK_ETW_KW_LOADER);    break;
    case CTK_ETW_KW_CLR:       CTK_ETW_EMIT_LEVELS(CTK_ETW_KW_CLR);       break;
    case CTK_ETW_KW_HANDOFF:   CTK_ETW_EMIT_LEVELS(CTK_ETW_KW_HANDOFF);   break;
    case CTK_ETW_KW_FATAL:     CTK_ETW_EMIT_LEVELS(CTK_ETW_KW_FATAL);     break;
    case CTK_ETW_KW_AUDIT:     CTK_ETW_EMIT_LEVELS(CTK_ETW_KW_AUDIT);     break;
    default:                   CTK_ETW_EMIT_LEVELS(CTK_ETW_KW_BOOTSTRAP); break;
    }
}

#undef CTK_ETW_EMIT_LEVELS
#undef CTK_ETW_EMIT
