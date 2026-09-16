#include <windows.h>

#include <stdint.h>
#include <stdio.h>

#include "ctk_app.h"
#include "ctk_error.h"

// Internal test seam declared by src/ctk_app_internal.h.  Keep this standalone
// harness ASCII-only so a legacy MSVC active code page cannot consume an UTF-8
// comment line-continuation before the declaration.
int ctk_invoke_token(void* cmd);

static volatile LONG g_dispatch_calls = 0;
static volatile LONG g_mode = 0;
static volatile LONG g_reentrant_terminate_rc = CTK_OK;
static void* g_command = 0;

enum TestDispatchMode
{
    TEST_DISPATCH_NORMAL = 0,
    TEST_DISPATCH_SEH = 1,
    TEST_DISPATCH_REENTRANT_TERMINATE = 2
};

static int32_t __cdecl test_dispatch(int32_t token)
{
    InterlockedIncrement(&g_dispatch_calls);
    if (g_mode == TEST_DISPATCH_SEH) {
        RaiseException(0xE0424242, 0, 0, 0);
    }
    if (g_mode == TEST_DISPATCH_REENTRANT_TERMINATE) {
        InterlockedExchange(&g_reentrant_terminate_rc, Ctk_CommandBridgeTerminate());
    }
    return token;
}

struct WrongThreadResult
{
    int32_t action_rc;
    int32_t action_id_was_zero;
    int32_t terminate_rc;
    int32_t status_rc;
};

static DWORD WINAPI wrong_thread_proc(void* raw)
{
    WrongThreadResult* result = (WrongThreadResult*)raw;
    void* command = (void*)1;
    result->action_rc = Ctk_CommandActionAdd("wrong_thread", 99, 5, 1, 0, &command);
    result->action_id_was_zero = command == 0 ? 1 : 0;
    result->terminate_rc = Ctk_CommandBridgeTerminate();
    int32_t token = 0;
    int32_t status = 0;
    result->status_rc = Ctk_LastCommandStatusGet(&token, &status);
    ctk_invoke_token(g_command); // must be inert before route/dispatch access.
    return 0;
}

static int fail(const char* expression, int line)
{
    fprintf(stderr, "FAILED line %d: %s\n", line, expression);
    return 1;
}

#define CHECK(expression) do { if (!(expression)) return fail(#expression, __LINE__); } while (0)

int main()
{
    CHECK(Ctk_CommandBridgeInitialize(test_dispatch) == CTK_OK);
    CHECK(Ctk_CommandBridgeInitialize(test_dispatch) != CTK_OK); // duplicate init

    // The complete host-style reservation is a contract. The second add is
    // rejected before its external-registration branch can allocate/register.
    CHECK(Ctk_CommandBridgeReserve(1) == CTK_OK);
    CHECK(Ctk_CommandActionAdd("capacity_one", 7, 5, 1, 0, &g_command) == CTK_OK);
    CHECK(g_command != 0);
    void* rejected_command = (void*)1;
    CHECK(Ctk_CommandActionAdd("must_not_register", 8, 5, 1, 0, &rejected_command) != CTK_OK);
    CHECK(rejected_command == 0);

    WrongThreadResult wrong = {};
    HANDLE thread = CreateThread(0, 0, wrong_thread_proc, &wrong, 0, 0);
    CHECK(thread != 0);
    CHECK(WaitForSingleObject(thread, INFINITE) == WAIT_OBJECT_0);
    CloseHandle(thread);
    CHECK(wrong.action_rc != CTK_OK && wrong.action_id_was_zero == 1);
    CHECK(wrong.terminate_rc != CTK_OK);
    CHECK(wrong.status_rc != CTK_OK);
    CHECK(g_dispatch_calls == 0);

    g_mode = TEST_DISPATCH_SEH;
    ctk_invoke_token(g_command); // SEH from the target cannot escape the trampoline.
    int32_t token = -1;
    int32_t status = -1;
    CHECK(Ctk_LastCommandStatusGet(&token, &status) == CTK_OK);
    CHECK(token == 7 && status == CTK_ERR_INTERNAL);

    g_mode = TEST_DISPATCH_REENTRANT_TERMINATE;
    ctk_invoke_token(g_command);
    CHECK(g_reentrant_terminate_rc != CTK_OK);
    const LONG calls_before_normal = g_dispatch_calls;
    g_mode = TEST_DISPATCH_NORMAL;
    ctk_invoke_token(g_command);
    CHECK(g_dispatch_calls == calls_before_normal + 1); // callback root survived reentrant terminate

    CHECK(Ctk_CommandBridgeTerminate() == CTK_OK);
    const LONG calls_before_inert = g_dispatch_calls;
    ctk_invoke_token(g_command);
    CHECK(g_dispatch_calls == calls_before_inert); // stale Creo command is inert
    CHECK(Ctk_CommandBridgeInitialize(test_dispatch) != CTK_OK); // never remap stale cmd IDs

    puts("ctk_app_lifetime_test passed");
    return 0;
}
