/*
 * host_log_paths.h - NativeHost 启动诊断日志路径决策与文件管理。
 *
 * 纯 Win32 实现，零 CRT 依赖（DllMain 早期可调）。
 * 决策树：env CTK_BOOTSTRAP_LOG_DIR > legacy CTK_HOST_LOG_DIR >
 * NativeHost.dll 同级 ..\logs\host\ > %LOCALAPPDATA%\CreoToolkit\logs\host\
 */
#pragma once
#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 解析 host log 目录到 out（NUL 结尾），自动递归创建。返回 0 成功，非 0 失败。
 * hinst_dll：传 DllMain 拿到的 hinstDLL；NULL 时跳过 dll-相对策略。 */
int host_log_resolve_dir(HINSTANCE hinst_dll, wchar_t* out, size_t out_count);

/* 通用 retention cleanup：扫单一目录（不递归）里 prefix 开头（nullptr/"" = 不筛前缀）
 * + suffix 结尾（如 L".jsonl" / L".pointer" / L".log"）+ mtime 早于 retention_days 的删除。
 * 供 bootstrap JSONL / leftover session-index / *.pointer 与
 * DllMain attach 证据（ctk-attach-p*.log，位于 %LOCALAPPDATA%\CreoToolkit）清理复用。
 * retention_days<=0 跳过；返回 0 成功 / 1 参数无效。 */
int host_log_cleanup_dir_by_mtime(const wchar_t* dir,
                                   const wchar_t* prefix,
                                   const wchar_t* suffix,
                                   int retention_days);

/* ====================================================================
 * OTel-shaped bootstrap evidence API
 * ==================================================================== */

/* ---- 可测基础单元（从 anonymous namespace 提出供单测直接调用） ---- */

/* FNV-1a 64-bit hash（纯算法，无系统调用）。
 * init 通常传 0xcbf29ce484222325（FNV offset basis）。 */
ULONGLONG host_log_fnv1a_64(const void* data, size_t len, ULONGLONG init);

/* 64-bit 值 → 16 lowercase hex wchar_t（不写 NUL，调用方确保 out 至少 16 元素）。 */
void host_log_u64_to_hex16(ULONGLONG v, wchar_t* out);

/* UTF-8 JSON 字符串转义：处理 \ " \n \r \t，<0x20 转 \u00XX。
 * out_cap 含 NUL。返回 0 成功，1 参数无效，2 容量不足。 */
int host_log_json_escape_a(const char* in, char* out, size_t out_cap);

/* bootstrapId: DllMain-safe，纯 Kernel32 + FNV-1a，32 hex chars + NUL。
 * 输入 hinst_dll = DllMain 拿到的 HINSTANCE；out_count >= 33。
 * 算法：(pid << 32 | tid) / (filetime hi << 32 | lo) / qpc / module base / interlocked seq
 * 经 FNV-1a 64-bit 双 hash 后拼接为 32 lowercase hex。非 CSPRNG，仅本地启动实例唯一性。
 * traceId（W3C 标准 128-bit CSPRNG）在 init 安全点用 BCryptGenRandom 生成。 */
int host_log_make_bootstrap_id(HINSTANCE hinst_dll,
                                wchar_t* out,
                                size_t out_count);

/* ISO-8601 UTC stamp 带毫秒：yyyyMMddTHHmmss.fffZ（20 chars + NUL）。
 * out_count >= 21。用 GetSystemTime（UTC），不用 GetLocalTime。 */
int host_log_make_iso_stamp(wchar_t* out, size_t out_count);

/* 写一条 JSONL 事件到指定文件（追加模式，FILE_FLAG_WRITE_THROUGH 每写强制落盘）。
 * event_json_utf8 必须是单行 UTF-8 JSON，**不带末尾换行**——本函数会追加 "\n"。
 * 返回 0 成功；非 0：1=参数无效，2=CreateFileW 失败，3=WriteFile 失败。
 * 每条事件独立 open/append/close，与 Creo 主进程崩溃恢复路径解耦。 */
int host_log_append_jsonl_event(const wchar_t* path,
                                 const char* event_json_utf8);

/* 原子写 pointer 文件（一行字符串，UTF-8 无 BOM）。
 * 实现：写 {dir}\{pointer_name}.tmp → MoveFileExW REPLACE_EXISTING（NTFS atomic rename）。
 * pointer_name 例如 "latest-session.pointer"。
 * content_utf8 一般是 bootstrap jsonl 文件名（无路径）。 */
int host_log_write_pointer(const wchar_t* dir,
                            const wchar_t* pointer_name,
                            const char* content_utf8);

/* 格式化 OTel-shaped JSONL 事件行（纯函数，无全局状态/IO）。
 * 所有字段由调用方传入；json_out 写入完整 JSON 行（不含末尾换行）。
 * body_utf8 可空（省略 body 字段）；内部对 body 做 JSON escape。
 * event_name / severity_text 必须 ASCII safe（不 escape）。
 * resource_json 为预构建 JSON 对象字符串（含 {}），空串时用 "{}"。
 * 返回：0 成功 / 1 参数无效 / 2 缓冲区不足。 */
int host_log_format_otel_event(
    const char* event_name,
    int severity_number,
    const char* severity_text,
    const char* body_utf8,
    const char* trace_id,
    const char* bootstrap_id,
    const char* resource_json,
    long seq,
    unsigned long long time_unix_nano,
    char* json_out,
    size_t json_out_cap);

#ifdef __cplusplus
}
#endif
