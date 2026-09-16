/*
 * host_probe.cpp - Manual debug harness: LoadLibraryW NativeHost and call
 * user_initialize. Not a CMake target; not shipped.
 *
 * This is NOT a no-Creo probe. Production user_initialize always calls
 * ctk_toolkit_touch() → ProToolkitApplTextPathGet, and NativeHost links
 * protk_dllmd_NU.lib, so LoadLibraryW needs Creo ucore* on the loader path.
 * Default CTK_HOST_METHOD is InitializeApp (not Initialize).
 */
#include <Windows.h>
#include <cstdio>

typedef int (*user_init_fn)(int, char**, char*, char*);

int wmain(int argc, wchar_t** argv)
{
    const wchar_t* dll = (argc > 1) ? argv[1]
        : L"CreoToolkit.NativeHost.dll";

    HMODULE h = LoadLibraryW(dll);
    if (h == nullptr) {
        wprintf(L"LoadLibraryW failed: GetLastError=%lu\n", GetLastError());
        return 100;
    }
    wprintf(L"LoadLibraryW ok: %ls\n", dll);

    user_init_fn fn = (user_init_fn)GetProcAddress(h, "user_initialize");
    if (fn == nullptr) {
        wprintf(L"GetProcAddress(user_initialize) failed: %lu\n", GetLastError());
        return 101;
    }

    int rc = fn(0, nullptr, nullptr, nullptr);
    wprintf(L"user_initialize returned %d\n", rc);
    return rc;
}
