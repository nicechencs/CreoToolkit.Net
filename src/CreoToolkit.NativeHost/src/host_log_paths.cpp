/*
 * host_log_paths.cpp - 纯 Win32 实现，零 CRT 依赖。
 * DllMain 早期可调：仅依赖 kernel32（GetEnvironmentVariableW / GetTempPathW /
 * GetModuleFileNameW / CreateDirectoryW / CreateFileW / wsprintfW），不调 shell32。
 * 去掉 SHGetFolderPathW 避免 loader-lock 死锁风险。
 */
#include <windows.h>
#include <cstdio>              /* _snprintf_s: host_log_format_otel_event（非 DllMain 路径） */

#include "host_log_paths.h"

namespace {

/* ---- 字符串/数字工具：手写，不用 CRT ---- */

size_t wstr_len(const wchar_t* s)
{
    size_t n = 0;
    if (s == nullptr) return 0;
    while (s[n] != L'\0') ++n;
    return n;
}

/* 追加 src 到 dst 末尾，自动维护长度 cap（包含 NUL 的容量）。返回是否成功。 */
bool wstr_append(wchar_t* dst, size_t cap, const wchar_t* src)
{
    if (dst == nullptr || src == nullptr || cap == 0) return false;
    size_t cur = wstr_len(dst);
    size_t add = wstr_len(src);
    if (cur + add + 1 > cap) return false;
    for (size_t i = 0; i < add; ++i) dst[cur + i] = src[i];
    dst[cur + add] = L'\0';
    return true;
}

bool wstr_copy(wchar_t* dst, size_t cap, const wchar_t* src)
{
    if (dst == nullptr || cap == 0) return false;
    dst[0] = L'\0';
    return wstr_append(dst, cap, src == nullptr ? L"" : src);
}

/* 把 unsigned 写成最小 width 位（前补 0），追加到 dst。返回是否成功。 */
bool wstr_append_uint(wchar_t* dst, size_t cap, unsigned int v, int min_width)
{
    wchar_t buf[16];
    int n = 0;
    if (v == 0) {
        buf[n++] = L'0';
    } else {
        wchar_t tmp[16];
        int m = 0;
        while (v > 0 && m < 15) {
            tmp[m++] = (wchar_t)(L'0' + (v % 10));
            v /= 10;
        }
        while (m > 0) buf[n++] = tmp[--m];
    }
    while (n < min_width && n < 15) {
        /* 前补 0 */
        for (int i = n; i > 0; --i) buf[i] = buf[i - 1];
        buf[0] = L'0';
        ++n;
    }
    buf[n] = L'\0';
    return wstr_append(dst, cap, buf);
}

bool wstr_ieq_prefix(const wchar_t* s, const wchar_t* prefix)
{
    if (s == nullptr || prefix == nullptr) return false;
    while (*prefix != L'\0') {
        wchar_t a = *s, b = *prefix;
        if (a >= L'A' && a <= L'Z') a = (wchar_t)(a - L'A' + L'a');
        if (b >= L'A' && b <= L'Z') b = (wchar_t)(b - L'A' + L'a');
        if (a != b) return false;
        ++s; ++prefix;
    }
    return true;
}

/* ---- 目录创建：递归 ---- */

void ensure_dir_recursive(const wchar_t* path)
{
    if (path == nullptr || path[0] == L'\0') return;
    wchar_t buf[MAX_PATH];
    if (!wstr_copy(buf, MAX_PATH, path)) return;

    /* 跳过盘符 / UNC 前缀 */
    size_t i = 0;
    size_t len = wstr_len(buf);
    if (len >= 2 && buf[1] == L':') i = 3;             /* 跳过 X:\ */
    else if (len >= 2 && buf[0] == L'\\' && buf[1] == L'\\') {
        /* UNC：跳过 \\server\share\ */
        i = 2;
        int slashes = 0;
        while (i < len && slashes < 2) {
            if (buf[i] == L'\\' || buf[i] == L'/') ++slashes;
            ++i;
        }
    }

    for (; i <= len; ++i) {
        wchar_t c = buf[i];
        if (c == L'\\' || c == L'/' || c == L'\0') {
            buf[i] = L'\0';
            if (buf[0] != L'\0') {
                BOOL ok = CreateDirectoryW(buf, nullptr);
                if (!ok) {
                    DWORD e = GetLastError();
                    if (e != ERROR_ALREADY_EXISTS) {
                        /* 失败但继续尝试下层（可能是中间组件无权限） */
                    }
                }
            }
            buf[i] = (c == L'\0') ? L'\0' : L'\\';
            if (c == L'\0') break;
        }
    }
}

/* ---- 路径决策 ---- */

/* 试写一个临时 probe 文件确认目录可写。FILE_FLAG_DELETE_ON_CLOSE 确保不留残留。
 * 仅看 ensure_dir_recursive 返回不可靠，
 * CreateDirectoryW 在只读位置（如 Program Files）失败被静默吞掉，
 * 上层只看返回成功会误把不可写目录当成最终选择。 */
bool dir_is_writable(const wchar_t* dir)
{
    /* probe 文件名带 pid:两个 Creo 并发启动同 dir 时,各自 probe 名不同,
     * 避免 sharing violation 误判 dir 不可写。
     * DELETE_ON_CLOSE 仍保证不留残留;FILE_SHARE_READ|WRITE|DELETE 三协享,
     * 即便其他进程同时启动也不阻塞。 */
    wchar_t probe[MAX_PATH];
    wchar_t pid_buf[24];
    if (!wstr_copy(probe, MAX_PATH, dir)) return false;
    if (!wstr_append(probe, MAX_PATH, L"\\.ctk_writable_probe-")) return false;
    /* 手写 pid → wstring（不用 wsprintfW，避免 user32 依赖；保持 kernel32-only loader-safe）。 */
    {
        DWORD pid = GetCurrentProcessId();
        wchar_t tmp[24];
        size_t pos = 0;
        if (pid == 0) tmp[pos++] = L'0';
        else {
            wchar_t rev[24];
            size_t rpos = 0;
            while (pid > 0 && rpos < 23) { rev[rpos++] = (wchar_t)(L'0' + (pid % 10)); pid /= 10; }
            while (rpos > 0) tmp[pos++] = rev[--rpos];
        }
        tmp[pos] = L'\0';
        if (!wstr_copy(pid_buf, 24, tmp)) return false;
    }
    if (!wstr_append(probe, MAX_PATH, pid_buf)) return false;
    HANDLE h = CreateFileW(probe, GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                            nullptr,
                            CREATE_ALWAYS,
                            FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE,
                            nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    CloseHandle(h);
    return true;
}

bool dll_relative_log_dir(HINSTANCE hinst_dll, wchar_t* out, size_t out_count)
{
    if (hinst_dll == nullptr || out == nullptr || out_count == 0) return false;
    wchar_t dll_path[MAX_PATH];
    DWORD n = GetModuleFileNameW(hinst_dll, dll_path, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return false;

    /* 截掉文件名：得到 <deploy>\native\ 这一级（不含末尾反斜杠） */
    wchar_t* slash = nullptr;
    for (size_t i = 0; dll_path[i] != L'\0'; ++i) {
        if (dll_path[i] == L'\\' || dll_path[i] == L'/') slash = &dll_path[i];
    }
    if (slash == nullptr) return false;
    *slash = L'\0';

    /* 再截一级：得到 <deploy> */
    wchar_t* slash2 = nullptr;
    for (size_t i = 0; dll_path[i] != L'\0'; ++i) {
        if (dll_path[i] == L'\\' || dll_path[i] == L'/') slash2 = &dll_path[i];
    }
    if (slash2 == nullptr) return false;
    *slash2 = L'\0';

    if (!wstr_copy(out, out_count, dll_path)) return false;
    if (!wstr_append(out, out_count, L"\\logs\\host")) return false;
    return true;
}

bool localappdata_log_dir(wchar_t* out, size_t out_count)
{
    wchar_t base[MAX_PATH];
    base[0] = L'\0';

    /* DllMain 阶段持有 loader lock，绝对禁止调 SHGetFolderPathW（shell32），
     * 否则可能死锁/启动失败。改为仅依赖 env LOCALAPPDATA（kernel32，loader-safe），
     * 失败 fall back GetTempPathW（也是 kernel32）。
     * 在现代 Windows session 中 LOCALAPPDATA env 几乎一直可用；service 等异常环境用 TEMP 兜底。 */
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", base, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) {
        n = GetTempPathW(MAX_PATH, base);
        if (n == 0 || n >= MAX_PATH) return false;
        /* GetTempPathW 末尾带 \，去掉避免双 \ */
        if (n > 0 && (base[n - 1] == L'\\' || base[n - 1] == L'/')) base[n - 1] = L'\0';
    }
    if (!wstr_copy(out, out_count, base)) return false;
    if (!wstr_append(out, out_count, L"\\CreoToolkit\\logs\\host")) return false;
    return true;
}

} // namespace

/* ---- 公共 API ---- */

extern "C" int host_log_resolve_dir(HINSTANCE hinst_dll, wchar_t* out, size_t out_count)
{
    if (out == nullptr || out_count == 0) return 1;
    out[0] = L'\0';

    /* 优先 1：env。新名 CTK_BOOTSTRAP_LOG_DIR 优先，回退旧名 CTK_HOST_LOG_DIR（保留兼容别名）。
     * 显式 env 指向不可写时不能直接接受,否则所有 bootstrap 证据静默丢失
     * (用户拼错 env 时极难发现)。失败 fall through 到 dll-相对 / LOCALAPPDATA,
     * 日志至少能落地;想强制只用 env 的用户应让 env 路径本身可写。 */
    DWORD n = GetEnvironmentVariableW(L"CTK_BOOTSTRAP_LOG_DIR", out, (DWORD)out_count);
    if (!(n > 0 && n < out_count)) {
        n = GetEnvironmentVariableW(L"CTK_HOST_LOG_DIR", out, (DWORD)out_count);
    }
    if (n > 0 && n < out_count) {
        ensure_dir_recursive(out);
        if (dir_is_writable(out)) return 0;
        out[0] = L'\0';
    }

    /* 优先 2：dll 上级 \logs\host\。CreateDirectoryW + 写一个 probe 文件确认可写；
     * 只读安装位置（如 Program Files）下创建静默失败，必须 fall through 到 LOCALAPPDATA。 */
    if (dll_relative_log_dir(hinst_dll, out, out_count)) {
        ensure_dir_recursive(out);
        if (dir_is_writable(out)) return 0;
        out[0] = L'\0';  /* 不可写，让下一候选覆盖 */
    }

    /* 优先 3：LOCALAPPDATA\CreoToolkit\logs\host\。同样验证可写，否则失败。 */
    if (localappdata_log_dir(out, out_count)) {
        ensure_dir_recursive(out);
        if (dir_is_writable(out)) return 0;
        out[0] = L'\0';
    }

    return 2;
}

/* ---- retention：扫单一目录，按 mtime cutoff 删除 ---- */

namespace {

bool filetime_before(const FILETIME& a, const FILETIME& cutoff)
{
    ULARGE_INTEGER ua, uc;
    ua.LowPart = a.dwLowDateTime;  ua.HighPart = a.dwHighDateTime;
    uc.LowPart = cutoff.dwLowDateTime; uc.HighPart = cutoff.dwHighDateTime;
    return ua.QuadPart < uc.QuadPart;
}

/* 大小写不敏感判断 name 是否以 suffix 结尾。suffix 空 → 视为匹配。 */
bool ends_with_ci(const wchar_t* name, const wchar_t* suffix)
{
    if (suffix == nullptr || suffix[0] == L'\0') return true;
    size_t n = wstr_len(name);
    size_t s = wstr_len(suffix);
    if (n < s) return false;
    const wchar_t* tail = name + (n - s);
    for (size_t i = 0; i < s; ++i) {
        wchar_t a = tail[i], b = suffix[i];
        if (a >= L'A' && a <= L'Z') a = (wchar_t)(a - L'A' + L'a');
        if (b >= L'A' && b <= L'Z') b = (wchar_t)(b - L'A' + L'a');
        if (a != b) return false;
    }
    return true;
}

/* 算 mtime cutoff = now - retention_days（100ns tick）。retention_days 已保证 >0。
 * 返回 false = 极端时间下溢，调用方应跳过清理。 */
bool compute_mtime_cutoff(int retention_days, FILETIME& cutoff)
{
    FILETIME now_ft;
    GetSystemTimeAsFileTime(&now_ft);
    ULARGE_INTEGER now_u;
    now_u.LowPart = now_ft.dwLowDateTime;
    now_u.HighPart = now_ft.dwHighDateTime;
    /* 1 day = 24*60*60 * 1e7 100ns ticks = 864000000000 */
    ULONGLONG day_ticks = 864000000000ULL;
    if ((ULONGLONG)retention_days > now_u.QuadPart / day_ticks) return false;
    now_u.QuadPart -= day_ticks * (ULONGLONG)retention_days;
    cutoff.dwLowDateTime = now_u.LowPart;
    cutoff.dwHighDateTime = now_u.HighPart;
    return true;
}

/* 扫单一目录里 prefix 开头（空/nullptr = 不筛前缀）+ suffix 结尾 + mtime < cutoff 的文件，
 * DeleteFileW。不递归子目录。 */
void cleanup_one_dir(const wchar_t* dir,
                     const wchar_t* prefix,
                     const wchar_t* suffix,
                     const FILETIME& cutoff)
{
    if (dir == nullptr || dir[0] == L'\0') return;
    bool has_prefix = (prefix != nullptr && prefix[0] != L'\0');
    bool has_suffix = (suffix != nullptr && suffix[0] != L'\0');

    wchar_t pattern[MAX_PATH];
    pattern[0] = L'\0';
    if (!wstr_append(pattern, MAX_PATH, dir)) return;
    if (!wstr_append(pattern, MAX_PATH, L"\\*")) return;
    if (has_suffix && !wstr_append(pattern, MAX_PATH, suffix)) return;

    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW(pattern, &fd);
    if (h == INVALID_HANDLE_VALUE) return;

    do {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
        if (has_prefix && !wstr_ieq_prefix(fd.cFileName, prefix)) continue;
        if (!ends_with_ci(fd.cFileName, suffix)) continue;
        if (!filetime_before(fd.ftLastWriteTime, cutoff)) continue;

        wchar_t full[MAX_PATH];
        full[0] = L'\0';
        if (!wstr_append(full, MAX_PATH, dir)) continue;
        if (!wstr_append(full, MAX_PATH, L"\\")) continue;
        if (!wstr_append(full, MAX_PATH, fd.cFileName)) continue;
        DeleteFileW(full);
    } while (FindNextFileW(h, &fd));
    FindClose(h);
}

} // namespace

extern "C" int host_log_cleanup_dir_by_mtime(const wchar_t* dir,
                                             const wchar_t* prefix,
                                             const wchar_t* suffix,
                                             int retention_days)
{
    if (retention_days <= 0) return 0;
    if (dir == nullptr) return 1;

    FILETIME cutoff;
    if (!compute_mtime_cutoff(retention_days, cutoff)) return 0;

    /* 单目录、不递归；prefix 空 = 只按 suffix 筛。 */
    cleanup_one_dir(dir, prefix, suffix, cutoff);
    return 0;
}

/* ====================================================================
 * OTel-shaped bootstrap evidence API 实现
 * 全部纯 Kernel32，DllMain 安全；UTF-8 JSON 输出。
 * ==================================================================== */

/* ---- FNV-1a / hex 工具（从 anonymous namespace 提出为 extern "C" 可测单元） ---- */

/* 常量保留文件作用域，供 host_log_make_bootstrap_id 使用 */
static const ULONGLONG kFnvOffset64 = 0xcbf29ce484222325ULL;
static const ULONGLONG kFnvPrime64  = 0x00000100000001b3ULL;

extern "C" ULONGLONG host_log_fnv1a_64(const void* data, size_t len, ULONGLONG init)
{
    const unsigned char* p = (const unsigned char*)data;
    ULONGLONG h = init;
    for (size_t i = 0; i < len; ++i) {
        h ^= (ULONGLONG)p[i];
        h *= kFnvPrime64;
    }
    return h;
}

extern "C" void host_log_u64_to_hex16(ULONGLONG v, wchar_t* out)
{
    static const wchar_t kHex[] = L"0123456789abcdef";
    for (int i = 15; i >= 0; --i) {
        out[15 - i] = kHex[(v >> (i * 4)) & 0xf];
    }
}

extern "C" int host_log_json_escape_a(const char* in, char* out, size_t out_cap)
{
    if (in == nullptr || out == nullptr || out_cap == 0) return 1;
    size_t pos = 0;
    for (size_t i = 0; in[i] != '\0'; ++i) {
        unsigned char c = (unsigned char)in[i];
        const char* esc = nullptr;
        char unicode_buf[8];
        switch (c) {
            case '\\': esc = "\\\\"; break;
            case '"':  esc = "\\\""; break;
            case '\n': esc = "\\n";  break;
            case '\r': esc = "\\r";  break;
            case '\t': esc = "\\t";  break;
            default:
                if (c < 0x20) {
                    static const char kHex[] = "0123456789abcdef";
                    unicode_buf[0] = '\\'; unicode_buf[1] = 'u';
                    unicode_buf[2] = '0';  unicode_buf[3] = '0';
                    unicode_buf[4] = kHex[(c >> 4) & 0xf];
                    unicode_buf[5] = kHex[c & 0xf];
                    unicode_buf[6] = '\0';
                    esc = unicode_buf;
                }
                break;
        }
        if (esc != nullptr) {
            for (size_t k = 0; esc[k] != '\0'; ++k) {
                if (pos + 1 >= out_cap) return 2;
                out[pos++] = esc[k];
            }
        } else {
            if (pos + 1 >= out_cap) return 2;
            out[pos++] = (char)c;
        }
    }
    out[pos] = '\0';
    return 0;
}

namespace {

/* bootstrapId 序列号（DllMain 安全，Interlocked） */
static volatile LONG g_bootstrap_seq = 0;

} // namespace

extern "C" int host_log_make_bootstrap_id(HINSTANCE hinst_dll,
                                           wchar_t* out,
                                           size_t out_count)
{
    if (out == nullptr || out_count < 33) return 1;

    /* 五元组采样（全部 Kernel32，DllMain 安全） */
    DWORD pid = GetCurrentProcessId();
    DWORD tid = GetCurrentThreadId();
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    ULONGLONG mod = (ULONGLONG)(ULONG_PTR)hinst_dll;
    LONG seq = InterlockedIncrement(&g_bootstrap_seq);

    ULONGLONG hi  = ((ULONGLONG)pid << 32) | (ULONGLONG)tid;
    ULONGLONG mid = ((ULONGLONG)ft.dwHighDateTime << 32) | (ULONGLONG)ft.dwLowDateTime;
    ULONGLONG lo  = (ULONGLONG)qpc.QuadPart;
    ULONGLONG seq64 = (ULONGLONG)(ULONG)seq;

    /* 双 hash：h0 吸收 pid/tid/filetime；h1 吸收 qpc/module/seq */
    ULONGLONG h0 = host_log_fnv1a_64(&hi, sizeof(hi), kFnvOffset64);
    h0 = host_log_fnv1a_64(&mid, sizeof(mid), h0);
    ULONGLONG h1 = host_log_fnv1a_64(&lo, sizeof(lo), kFnvOffset64);
    h1 = host_log_fnv1a_64(&mod, sizeof(mod), h1);
    h1 = host_log_fnv1a_64(&seq64, sizeof(seq64), h1);

    host_log_u64_to_hex16(h0, &out[0]);
    host_log_u64_to_hex16(h1, &out[16]);
    out[32] = L'\0';
    return 0;
}

extern "C" int host_log_make_iso_stamp(wchar_t* out, size_t out_count)
{
    if (out == nullptr || out_count < 21) return 1;
    SYSTEMTIME st;
    GetSystemTime(&st);   /* UTC，与 GetLocalTime 区别 */

    out[0] = L'\0';
    /* yyyyMMddTHHmmss.fffZ */
    if (!wstr_append_uint(out, out_count, (unsigned int)st.wYear, 4)) return 2;
    if (!wstr_append_uint(out, out_count, (unsigned int)st.wMonth, 2)) return 2;
    if (!wstr_append_uint(out, out_count, (unsigned int)st.wDay, 2)) return 2;
    if (!wstr_append(out, out_count, L"T")) return 2;
    if (!wstr_append_uint(out, out_count, (unsigned int)st.wHour, 2)) return 2;
    if (!wstr_append_uint(out, out_count, (unsigned int)st.wMinute, 2)) return 2;
    if (!wstr_append_uint(out, out_count, (unsigned int)st.wSecond, 2)) return 2;
    if (!wstr_append(out, out_count, L".")) return 2;
    if (!wstr_append_uint(out, out_count, (unsigned int)st.wMilliseconds, 3)) return 2;
    if (!wstr_append(out, out_count, L"Z")) return 2;
    return 0;
}

extern "C" int host_log_append_jsonl_event(const wchar_t* path,
                                            const char* event_json_utf8)
{
    if (path == nullptr || event_json_utf8 == nullptr) return 1;

    /* FILE_FLAG_WRITE_THROUGH：每写强制落盘，bootstrap 阶段事件少，simplicity > tiny perf。
     * 与崩溃恢复路径解耦——即使 Creo 在我写 handle 时崩溃，前面事件至少已落盘。
     * FILE_SHARE_READ | FILE_SHARE_WRITE：允许多 Creo 实例并发追加同一文件场景（极少见）。 */
    HANDLE h = CreateFileW(path,
                            FILE_APPEND_DATA,
                            FILE_SHARE_READ | FILE_SHARE_WRITE,
                            nullptr,
                            OPEN_ALWAYS,
                            FILE_FLAG_WRITE_THROUGH | FILE_ATTRIBUTE_NORMAL,
                            nullptr);
    if (h == INVALID_HANDLE_VALUE) return 2;

    /* 计算 UTF-8 字节长度（含末尾追加的 \n）。 */
    size_t json_len = 0;
    while (event_json_utf8[json_len] != '\0') ++json_len;
    if (json_len == 0) {
        CloseHandle(h);
        return 1;
    }

    DWORD written = 0;
    BOOL ok = WriteFile(h,
                        event_json_utf8,
                        (DWORD)json_len,
                        &written,
                        nullptr);
    if (ok && written == (DWORD)json_len) {
        const char newline = '\n';
        DWORD nl_written = 0;
        ok = WriteFile(h, &newline, 1, &nl_written, nullptr) && nl_written == 1;
    } else {
        ok = FALSE;
    }
    CloseHandle(h);
    return ok ? 0 : 3;
}

extern "C" int host_log_write_pointer(const wchar_t* dir,
                                       const wchar_t* pointer_name,
                                       const char* content_utf8)
{
    if (dir == nullptr || pointer_name == nullptr || content_utf8 == nullptr) return 1;

    /* {dir}\{pointer_name}.tmp → MoveFileExW REPLACE_EXISTING（NTFS atomic rename）。 */
    wchar_t final_path[MAX_PATH];
    final_path[0] = L'\0';
    if (!wstr_append(final_path, MAX_PATH, dir)) return 1;
    if (!wstr_append(final_path, MAX_PATH, L"\\")) return 1;
    if (!wstr_append(final_path, MAX_PATH, pointer_name)) return 1;

    wchar_t tmp_path[MAX_PATH];
    tmp_path[0] = L'\0';
    if (!wstr_append(tmp_path, MAX_PATH, final_path)) return 1;
    if (!wstr_append(tmp_path, MAX_PATH, L".tmp")) return 1;

    /* 写 tmp（覆盖）+ WRITE_THROUGH 强制落盘后再 rename。 */
    HANDLE h = CreateFileW(tmp_path,
                            GENERIC_WRITE,
                            FILE_SHARE_READ,
                            nullptr,
                            CREATE_ALWAYS,
                            FILE_FLAG_WRITE_THROUGH | FILE_ATTRIBUTE_NORMAL,
                            nullptr);
    if (h == INVALID_HANDLE_VALUE) return 2;

    size_t content_len = 0;
    while (content_utf8[content_len] != '\0') ++content_len;

    DWORD written = 0;
    BOOL ok = TRUE;
    if (content_len > 0) {
        ok = WriteFile(h, content_utf8, (DWORD)content_len, &written, nullptr);
        if (!ok || written != (DWORD)content_len) ok = FALSE;
    }
    CloseHandle(h);
    if (!ok) {
        DeleteFileW(tmp_path);
        return 3;
    }

    /* NTFS rename 是 metadata 操作，atomic。失败时清理 tmp。 */
    if (!MoveFileExW(tmp_path, final_path, MOVEFILE_REPLACE_EXISTING)) {
        DeleteFileW(tmp_path);
        return 4;
    }
    return 0;
}

extern "C" int host_log_format_otel_event(
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
    size_t json_out_cap)
{
    if (event_name == nullptr || severity_text == nullptr ||
        trace_id == nullptr || bootstrap_id == nullptr ||
        json_out == nullptr || json_out_cap == 0) return 1;

    const char* resource = (resource_json != nullptr && resource_json[0] != '\0')
                           ? resource_json : "{}";

    /* body escape：2048 = 调用方最大 body 1024 × 反斜杠转义最坏 ×2（Windows 路径证据不许截丢）。 */
    char body_escaped[2048];
    const char* body_safe = "";
    bool has_body = (body_utf8 != nullptr);
    if (has_body) {
        if (host_log_json_escape_a(body_utf8, body_escaped, sizeof(body_escaped)) != 0) {
            body_escaped[0] = '\0';
        }
        body_safe = body_escaped;
    }

    /* _snprintf_s + _TRUNCATE: 缓冲区不足时截断返回 -1，不触发 abort。 */
    int n;
    if (has_body) {
        n = _snprintf_s(json_out, json_out_cap, _TRUNCATE,
            "{\"timeUnixNano\":\"%llu\","
            "\"observedTimeUnixNano\":\"%llu\","
            "\"traceId\":\"%s\","
            "\"spanId\":\"\","
            "\"severityText\":\"%s\","
            "\"severityNumber\":%d,"
            "\"eventName\":\"%s\","
            "\"body\":\"%s\","
            "\"resource\":%s,"
            "\"attributes\":{\"bootstrapId\":\"%s\"},"
            "\"seq\":%ld}",
            time_unix_nano, time_unix_nano, trace_id, severity_text, severity_number,
            event_name, body_safe, resource, bootstrap_id, seq);
    } else {
        n = _snprintf_s(json_out, json_out_cap, _TRUNCATE,
            "{\"timeUnixNano\":\"%llu\","
            "\"observedTimeUnixNano\":\"%llu\","
            "\"traceId\":\"%s\","
            "\"spanId\":\"\","
            "\"severityText\":\"%s\","
            "\"severityNumber\":%d,"
            "\"eventName\":\"%s\","
            "\"resource\":%s,"
            "\"attributes\":{\"bootstrapId\":\"%s\"},"
            "\"seq\":%ld}",
            time_unix_nano, time_unix_nano, trace_id, severity_text, severity_number,
            event_name, resource, bootstrap_id, seq);
    }
    if (n < 0 || (size_t)n >= json_out_cap) return 2;
    return 0;
}
