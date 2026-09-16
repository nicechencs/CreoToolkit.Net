#pragma once

#include <Windows.h>

namespace ctk {
namespace clr_host {

struct InvokeResult {
    bool executed = false;
    DWORD ret_code = 0;
    HRESULT hr = E_FAIL;
};

/*
 * CLR 4 desktop: CLRCreateInstance → GetRuntime(v4.0.30319) → Start →
 * ExecuteInDefaultAppDomain. Start is process-lifetime; do not Stop().
 * If CLR 4 is already loaded, Start still returns success and Execute attaches.
 */
InvokeResult execute(
    const wchar_t* assembly_path,
    const wchar_t* type_name,
    const wchar_t* method_name,
    const wchar_t* argument);

bool is_started() noexcept;

} // namespace clr_host
} // namespace ctk
