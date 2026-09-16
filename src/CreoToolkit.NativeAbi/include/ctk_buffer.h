/*
 * ctk_buffer.h - L1 值信封，用于跨 C ABI 传递平坦数据。
 * CtkBuffer 持有 CRT 分配块；分配时值拷贝，释放经 Ctk_FreeBuffer（同一堆）。
 */
#ifndef CTK_BUFFER_H
#define CTK_BUFFER_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct CtkBuffer {
    void*   data;       /* CRT 分配块，count==0 时为 NULL */
    int32_t count;      /* 元素数量 */
    int32_t elem_size;  /* 单个元素字节数 */
} CtkBuffer;

#ifdef __cplusplus
}
#endif

#endif /* CTK_BUFFER_H */
