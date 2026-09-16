/*
 * ctk_etw_provider.h - NativeHost ETW TraceLogging provider 声明（轨道 B）。
 *
 * 声明 provider 句柄、keyword 位图、init/shutdown 入口与事件发射函数。
 * ctk_etw_write_event 由 write_otel_event（host_entry.cpp）单点调用，
 * 与 JSONL 轨道 A 同源同事件。
 *
 * Provider 标识（编译期固定，跨编译器稳定）：
 *   name = "com.creotoolkit.Creo.NativeHost"
 *   GUID = {6181F760-2290-4F7B-98A1-825CD0E1468B}
 *
 * Keyword 位图：低 48 bit 业务可用，高 16 bit 为 Microsoft 保留。
 */
#pragma once

#include <Windows.h>
#include <TraceLoggingProvider.h>

/*
 * VS2015 Update 3 supports /utf-8, but its compiler rejects the Windows SDK
 * TraceLogging header's nested execution_character_set pragmas when that flag
 * is active (C3437). The translation unit is already UTF-8, so suppress only
 * the redundant internal scope macros on VC140. Newer MSVC toolsets keep the
 * SDK defaults untouched.
 */
#if defined(_MSC_VER) && _MSC_VER == 1900
#  ifdef _tlgPragmaUtf8Begin
#    undef _tlgPragmaUtf8Begin
#    define _tlgPragmaUtf8Begin
#  endif
#  ifdef _tlgPragmaUtf8End
#    undef _tlgPragmaUtf8End
#    define _tlgPragmaUtf8End
#  endif
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Provider 句柄声明；定义见 ctk_etw_provider.cpp。 */
TRACELOGGING_DECLARE_PROVIDER(g_ctk_native_host_provider);

/* Keyword 位图（64-bit）；reader 端用 keyword 过滤可大幅降低订阅开销。 */
#define CTK_ETW_KW_BOOTSTRAP   0x0000000000000001ull  /* DllMain → init 期事件 */
#define CTK_ETW_KW_LIFECYCLE   0x0000000000000002ull  /* 长生命周期 init/shutdown */
#define CTK_ETW_KW_LOADER      0x0000000000000004ull  /* 模块加载/依赖探测 */
#define CTK_ETW_KW_CLR         0x0000000000000008ull  /* mscoree/CLR 4 加载 */
#define CTK_ETW_KW_HANDOFF     0x0000000000000010ull  /* bootstrap → managed 交接 */
#define CTK_ETW_KW_FATAL       0x0000000000000020ull  /* 致命错误 */
#define CTK_ETW_KW_AUDIT       0x0000000000000040ull  /* hash chain / 审计 */
/* 0x0000000000000080 ~ 0x0000800000000000 留给后续业务类别（低 48 bit 业务空间）。 */
/* 0x0001000000000000 ~ 0x8000000000000000 高 16 bit 为 Microsoft 保留，禁占用。 */

/*
 * ctk_etw_init - 注册 ETW provider。
 *   必须在 *非 DllMain* 调用（loader lock 期注册 ETW 不安全）；
 *   由 user_initialize 调度。
 *   返回 0 表示成功；负值表示 TraceLoggingRegister 失败（HRESULT 取负）。
 *   幂等：重复调用结果不可预期，由调用方保证只调一次。
 */
int ctk_etw_init(void);

/*
 * ctk_etw_shutdown - 注销 ETW provider。
 *   必须在 *非 DllMain* 调用；由 user_terminate 调度。
 *   幂等：未注册或已注销时直接 noop。
 */
void ctk_etw_shutdown(void);

/*
 * ctk_etw_write_event - 发射一条 ETW 事件（单点接线）。
 *   eventName → keyword 按前缀推导后编译期分发（keyword × level 组合展开），
 *   会话级 -matchanykeyword / -level 过滤在 ETW 内核层生效。
 *   level 由 severityNumber 映射：>=21 CRITICAL(1) / >=17 ERROR(2) /
 *   >=13 WARNING(3) / >=9 INFO(4) / 其余 VERBOSE(5)。
 *   provider 未注册时 noop。非 DllMain 路径调用，不进 fatal crash 路径。
 */
void ctk_etw_write_event(const char* event_name,
                         int severity_number,
                         const char* severity_text,
                         const char* trace_id,
                         const char* body_utf8);

/*
 * ctk_etw_keyword_for_event - 从 eventName 前缀推导 keyword 位图。
 *   "ctk.native_host.lifecycle.*" → LIFECYCLE, 等等；
 *   无匹配时返回 BOOTSTRAP（catch-all）。
 */
unsigned long long ctk_etw_keyword_for_event(const char* event_name);

#ifdef __cplusplus
} /* extern "C" */
#endif
