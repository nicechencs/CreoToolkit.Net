#include "clr_host_loader.h"

#include <metahost.h>

#pragma comment(lib, "mscoree.lib")

namespace ctk {
namespace clr_host {
namespace {

ICLRRuntimeHost* g_runtime_host = nullptr;
bool g_started = false;

const wchar_t* kRuntimeVersion = L"v4.0.30319";

bool ensure_runtime_host(HRESULT* hr_out)
{
    if (g_runtime_host != nullptr) {
        *hr_out = S_OK;
        return true;
    }

    ICLRMetaHost* meta = nullptr;
    ICLRRuntimeInfo* info = nullptr;
    HRESULT hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&meta);
    if (FAILED(hr) || meta == nullptr) {
        *hr_out = hr;
        return false;
    }

    hr = meta->GetRuntime(kRuntimeVersion, IID_ICLRRuntimeInfo, (LPVOID*)&info);
    if (FAILED(hr) || info == nullptr) {
        meta->Release();
        *hr_out = hr;
        return false;
    }

    BOOL loadable = FALSE;
    hr = info->IsLoadable(&loadable);
    if (FAILED(hr) || !loadable) {
        info->Release();
        meta->Release();
        *hr_out = FAILED(hr) ? hr : E_FAIL;
        return false;
    }

    ICLRRuntimeHost* host = nullptr;
    hr = info->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&host);
    info->Release();
    meta->Release();
    if (FAILED(hr) || host == nullptr) {
        *hr_out = hr;
        return false;
    }

    hr = host->Start();
    /* Already-started CLR still reports success from Start in typical in-process hosts. */
    if (FAILED(hr)) {
        host->Release();
        *hr_out = hr;
        return false;
    }

    g_runtime_host = host;
    g_started = true;
    *hr_out = S_OK;
    return true;
}

} // namespace

InvokeResult execute(
    const wchar_t* assembly_path,
    const wchar_t* type_name,
    const wchar_t* method_name,
    const wchar_t* argument)
{
    InvokeResult result;
    HRESULT hr = E_FAIL;
    if (!ensure_runtime_host(&hr) || g_runtime_host == nullptr) {
        result.hr = hr;
        return result;
    }

    DWORD ret = 0;
    hr = g_runtime_host->ExecuteInDefaultAppDomain(
        assembly_path,
        type_name,
        method_name,
        argument != nullptr ? argument : L"",
        &ret);
    result.hr = hr;
    if (SUCCEEDED(hr)) {
        result.executed = true;
        result.ret_code = ret;
    }
    return result;
}

bool is_started() noexcept
{
    return g_started;
}

} // namespace clr_host
} // namespace ctk
