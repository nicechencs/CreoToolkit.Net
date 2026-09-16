/*
 * toolkit_touch.cpp - 触达 Creo TOOLKIT 运行时。
 *
 * 作用(关键): host_entry.cpp 只做 mscoree/CLR 4 宿主, 不调任何 Pro* 函数, 链接器因此把
 * protk_dllmd_NU/ucore/udata 全丢弃 → 产出的 DLL 不依赖 ucore46.dll → Creo 不把它当合法
 * TOOLKIT app, 加载 DLL(DllMain 跑)后却**不调用 user_initialize**, 报 PRO_TK_GENERAL_ERROR。
 * 本文件引用一个 TOOLKIT API(ProToolkitApplTextPathGet), 强制链接 TOOLKIT 运行时(ucore46 进 imports),
 * 并被 user_initialize 调用一次(满足 PTC "user_initialize 须至少调一个 TOOLKIT API" 的要求);
 * 同时验证 protk.dat 的 text_dir 可由 Creo 取回。
 *
 * 隔离: 本文件只含 Creo 头, 不含 Windows.h/metahost 头, 避免与 host_entry.cpp 的宿主头冲突。
 */
#include <ProToolkit.h>
#include <ProUtil.h>

/* 触达 TOOLKIT: 取当前应用 text_dir, 无模型会话依赖, init 期可用。 */
extern "C" int ctk_toolkit_touch(void)
{
    ProPath text_path = {0};
    return (int)ProToolkitApplTextPathGet(text_path);
}
