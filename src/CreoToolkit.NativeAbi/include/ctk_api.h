/*
 * ctk_api.h - L1 公开 C ABI 接口面。
 *
 * 所有导出均为 extern "C" __declspec(dllexport)，x64 单一调用约定。
 * 本头文件是上层（L2 P/Invoke）绑定的唯一契约。
 */
#ifndef CTK_API_H
#define CTK_API_H

#include "ctk_error.h"
#include "ctk_buffer.h"
#include "ctk_handle.h"

#ifdef __cplusplus
#  define CTK_API extern "C" __declspec(dllexport)
#else
#  define CTK_API __declspec(dllexport)
#endif

/*
 * CreoToolkit_Free - 按描述符释放 native 持有块。
 * 按 h->kind 分发；CTK_OWNER_PROARRAY_OF_HANDLE 先逐元素经 h->elem_free 释放，
 * 再释放外层数组和描述符。NULL 安全 no-op；调用后句柄悬空，不可复用。
 */
CTK_API void CreoToolkit_Free(CtkHandle h);

/*
 * Ctk_FreeBuffer - 释放值信封的数据块。
 * NULL 或已空的缓冲均为安全 no-op。结构体本身由调用方持有；
 * 只释放 b->data 并将字段清零。
 */
CTK_API void Ctk_FreeBuffer(CtkBuffer* b);

/*
 * Ctk_AllocBuffer - 分配值信封并从 src 值拷贝 count * elem_size 字节。
 * src 为 NULL 时零初始化。成功返回 CTK_OK；out 不可为 NULL。
 */
CTK_API CtkError Ctk_AllocBuffer(const void* src, int32_t count,
                                 int32_t elem_size, CtkBuffer* out);

/*
 * Ctk_AllocOwnedArrayOfHandle - 构建 CTK_OWNER_PROARRAY_OF_HANDLE 描述符，
 * 持有 count 个内部指针槽（释放时各自经 elem_free）。槽位零初始化，调用方填充。
 * 分配失败返回 NULL。
 */
CTK_API CtkHandle Ctk_AllocOwnedArrayOfHandle(int32_t count, CtkElemFree elem_free);

#endif /* CTK_API_H */
