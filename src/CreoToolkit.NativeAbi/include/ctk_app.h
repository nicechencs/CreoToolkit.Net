#ifndef CTK_APP_H
#define CTK_APP_H

#include <stdint.h>

#ifdef __cplusplus
#  define CTK_APP_API extern "C" __declspec(dllexport)
#else
#  define CTK_APP_API __declspec(dllexport)
#endif

typedef int32_t (__cdecl *CtkManagedCommandDispatch)(int32_t token);

CTK_APP_API int32_t Ctk_CommandBridgeInitialize(CtkManagedCommandDispatch dispatch);
/* Reserve the complete command count before registration. This prevents a
 * successful ProCmdActionAdd from being followed by an allocating route insert.
 * The reservation is one-shot; legacy callers may omit it and are safely
 * preallocated one route at a time before each external registration. */
CTK_APP_API int32_t Ctk_CommandBridgeReserve(int32_t command_count);
CTK_APP_API int32_t Ctk_CommandBridgeTerminate(void);

CTK_APP_API int32_t Ctk_CommandActionAdd(const char* action_name, int32_t token,
                                         int32_t priority,
                                         int32_t allow_non_active, int32_t allow_accessory,
                                         void** out_cmd_id);

CTK_APP_API int32_t Ctk_LastCommandStatusGet(int32_t* out_token, int32_t* out_status);

/* Ctk_MessageDisplay / Ctk_CommandDesignate / Ctk_RibbonDefinitionfileLoad
 * / Ctk_MenubarPushbuttonAdd 已移除:
 * 不踩 L1 准入门, C# 端切直绑 Pro* (见 NativeMethods.ProApp.cs)。详见 ctk_app.cpp 说明。
 */

#endif /* CTK_APP_H */
