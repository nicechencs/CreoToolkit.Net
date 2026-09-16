#include "ctk_app.h"

#include "ctk_error.h"

#include <windows.h>

#include <cstdint>
#include <cstring>
#include <cwchar>
#include <new>
#include <vector>

#ifndef CTK_APP_TEST_NO_CREO
// This TU only needs the command registration API.
#include <ProUICmd.h>
#endif

// Internal test seam; deliberately not part of the public ABI.
#include "ctk_app_internal.h"

/*
 * A Creo command registration has process lifetime: there is no supported
 * ProCmdActionAdd counterpart which unregisters one command. Consequently
 * routes are never erased, and this bridge intentionally cannot be
 * reinitialized after termination. An old Creo command can therefore never
 * acquire a token belonging to a later managed application.
 *
 * Every entry point is owned by the thread which successfully initializes the
 * bridge. The two interlocked values are only the admission gate; all route
 * storage and the managed function pointer are touched exclusively by that
 * owner thread. No lock is held around a Pro* call or reverse P/Invoke.
 */
enum CtkBridgeState
{
    CTK_BRIDGE_UNBOUND = 0,
    CTK_BRIDGE_ACTIVE = 1,
    CTK_BRIDGE_TERMINATED = 2
};

struct CtkCommandRoute
{
    void* command;
    int32_t token;
};

static volatile LONG g_owner_thread = 0;
static volatile LONG g_state = CTK_BRIDGE_UNBOUND;
static CtkManagedCommandDispatch g_dispatch = 0;
static std::vector<CtkCommandRoute> g_routes;
static size_t g_route_count = 0;
static bool g_preallocation_declared = false;
static int32_t g_last_token = -1;
static int32_t g_last_status = -1;
static LONG g_callback_depth = 0;

#ifdef CTK_APP_TEST_NO_CREO
static uintptr_t g_next_test_cmd = 0;
#endif

static LONG ctk_read_interlocked(volatile LONG* value)
{
    return InterlockedCompareExchange(value, 0, 0);
}

static bool ctk_is_owner_thread()
{
    const LONG owner = ctk_read_interlocked(&g_owner_thread);
    return owner != 0 && owner == (LONG)GetCurrentThreadId();
}

static bool ctk_is_active_owner_thread()
{
    return ctk_is_owner_thread() &&
        ctk_read_interlocked(&g_state) == CTK_BRIDGE_ACTIVE;
}

static void ctk_set_last(int32_t token, int32_t status)
{
    // Owner-thread-only state; do not make a foreign callback race a status
    // query with the real Creo thread.
    g_last_token = token;
    g_last_status = status;
}

static bool ctk_lookup_token(void* cmd, int32_t* out_token)
{
    if (out_token == 0 || cmd == 0) {
        return false;
    }
    *out_token = -1;

    for (size_t i = 0; i < g_route_count; ++i) {
        if (g_routes[i].command == cmd) {
            *out_token = g_routes[i].token;
            return true;
        }
    }
    return false;
}

// Prepare route storage before an external command registration. When the
// managed host declared its complete capacity, exceeding it fails before
// ProCmdActionAdd. Old ABI callers which do not declare capacity retain safe
// per-command preparation, likewise before ProCmdActionAdd.
static int32_t ctk_prepare_route_slot()
{
    if (g_route_count < g_routes.size()) {
        return CTK_OK;
    }
    if (g_preallocation_declared) {
        return CTK_ERR_INTERNAL;
    }

    try {
        g_routes.resize(g_route_count + 1);
    }
    catch (...) {
        return CTK_ERR_OUT_OF_MEM;
    }
    return CTK_OK;
}

int ctk_invoke_token(void* cmd)
{
    // This check must precede both the route table and g_dispatch. A foreign
    // thread is inert rather than trying to make the native table concurrent.
    if (!ctk_is_active_owner_thread()) {
        return 0;
    }

    int32_t token = -1;
    if (!ctk_lookup_token(cmd, &token) || g_dispatch == 0) {
        ctk_set_last(token, CTK_ERR_BAD_ARG);
        return 0;
    }

    int32_t status = CTK_ERR_INTERNAL;
    InterlockedIncrement(&g_callback_depth);
    __try {
        // No lock is held during reverse P/Invoke. Terminate called from the
        // callback observes g_callback_depth and fails without dropping this
        // live delegate root.
        status = g_dispatch(token);
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        status = CTK_ERR_INTERNAL;
    }
    InterlockedDecrement(&g_callback_depth);

    ctk_set_last(token, status);
    return 0;
}

#ifndef CTK_APP_TEST_NO_CREO
static int ctk_cmd_action_trampoline(uiCmdCmdId command, uiCmdValue*, void*)
{
    return ctk_invoke_token((void*)command);
}

static uiCmdAccessState ctk_cmd_access_trampoline(uiCmdAccessMode)
{
    return ACCESS_AVAILABLE;
}
#endif

CTK_APP_API int32_t Ctk_CommandBridgeInitialize(CtkManagedCommandDispatch dispatch)
{
    if (dispatch == 0) {
        return CTK_ERR_BAD_ARG;
    }

    const LONG current = (LONG)GetCurrentThreadId();
    if (InterlockedCompareExchange(&g_owner_thread, current, 0) != 0) {
        // Reinitialization, including after terminate, is explicitly not
        // supported: existing Creo registrations retain their process identity.
        return CTK_ERR_INTERNAL;
    }

    // This is the sole transition out of UNBOUND. The owner is deliberately
    // retained for the DLL lifetime so wrong-thread calls stay rejected after
    // termination as well.
    g_dispatch = dispatch;
    g_last_token = -1;
    g_last_status = -1;
    InterlockedExchange(&g_state, CTK_BRIDGE_ACTIVE);
#ifdef CTK_APP_TEST_NO_CREO
    g_next_test_cmd = 0;
#endif
    return CTK_OK;
}

CTK_APP_API int32_t Ctk_CommandBridgeReserve(int32_t command_count)
{
    if (command_count < 0) {
        return CTK_ERR_BAD_ARG;
    }
    if (!ctk_is_active_owner_thread()) {
        return CTK_ERR_INTERNAL;
    }
    if (g_route_count != 0 || g_preallocation_declared) {
        return CTK_ERR_INTERNAL;
    }

    try {
        g_routes.resize((size_t)command_count);
    }
    catch (...) {
        return CTK_ERR_OUT_OF_MEM;
    }

    // A declared capacity is a contract, not a hint. A later overrun is
    // rejected before the un-unregisterable external registration call.
    g_preallocation_declared = true;
    return CTK_OK;
}

CTK_APP_API int32_t Ctk_CommandBridgeTerminate(void)
{
    if (!ctk_is_owner_thread()) {
        return CTK_ERR_INTERNAL;
    }
    if (ctk_read_interlocked(&g_state) == CTK_BRIDGE_TERMINATED) {
        return CTK_OK;
    }
    if (ctk_read_interlocked(&g_state) != CTK_BRIDGE_ACTIVE ||
        ctk_read_interlocked(&g_callback_depth) != 0) {
        // Fail fast rather than wait on the Creo thread or invalidate the
        // dispatch pointer while its managed callback frame is still live.
        return CTK_ERR_INTERNAL;
    }

    g_dispatch = 0;
    InterlockedExchange(&g_state, CTK_BRIDGE_TERMINATED);
    // Do not clear routes: Creo may still invoke its process-lifetime command;
    // the inactive trampoline is then deliberately inert.
    return CTK_OK;
}

CTK_APP_API int32_t Ctk_CommandActionAdd(const char* action_name, int32_t token,
                                         int32_t priority,
                                         int32_t allow_non_active, int32_t allow_accessory,
                                         void** out_cmd_id)
{
    if (out_cmd_id != 0) {
        *out_cmd_id = 0;
    }
    if (action_name == 0 || action_name[0] == '\0' || out_cmd_id == 0) {
        return CTK_ERR_BAD_ARG;
    }
    if (!ctk_is_active_owner_thread()) {
        return CTK_ERR_INTERNAL;
    }

    const int32_t slot_rc = ctk_prepare_route_slot();
    if (slot_rc != CTK_OK) {
        return slot_rc;
    }

#ifdef CTK_APP_TEST_NO_CREO
    (void)priority;
    (void)allow_non_active;
    (void)allow_accessory;
    void* cmd_id = (void*)(++g_next_test_cmd);
#else
    uiCmdCmdId cmd_id = 0;
    ProError err = ProCmdActionAdd((char*)action_name,
                                   ctk_cmd_action_trampoline,
                                   (uiCmdPriority)priority,
                                   ctk_cmd_access_trampoline,
                                   (ProBoolean)(allow_non_active != 0),
                                   (ProBoolean)(allow_accessory != 0),
                                   &cmd_id);
    if (err != PRO_TK_NO_ERROR) {
        return (int32_t)err;
    }
    if (cmd_id == 0) {
        return CTK_ERR_INTERNAL;
    }
#endif

    // The slot was allocated before ProCmdActionAdd. These assignments cannot
    // allocate or throw, so a successful Creo registration always has a route.
    g_routes[g_route_count].command = (void*)cmd_id;
    g_routes[g_route_count].token = token;
    ++g_route_count;
    *out_cmd_id = (void*)cmd_id;
    return CTK_OK;
}

CTK_APP_API int32_t Ctk_LastCommandStatusGet(int32_t* out_token, int32_t* out_status)
{
    if (out_token != 0) {
        *out_token = 0;
    }
    if (out_status != 0) {
        *out_status = 0;
    }
    if (out_token == 0 || out_status == 0) {
        return CTK_ERR_BAD_ARG;
    }
    // Status is owner-thread state too. Rejecting cross-thread diagnostics
    // avoids converting this tiny side channel into a data-racy API.
    if (!ctk_is_owner_thread()) {
        return CTK_ERR_INTERNAL;
    }

    *out_token = g_last_token;
    *out_status = g_last_status;
    return CTK_OK;
}

/* The remaining app-facing Pro* calls are direct C# bindings. This native
 * layer exists only for the command trampoline and its process-lifetime route
 * identity. */
