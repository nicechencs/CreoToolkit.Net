/*
 * ctk_buffer.cpp - L1 值信封分配与释放。
 *
 * CtkBuffer::data 由本 DLL 在 CRT 堆上分配，/MD 下与调用方共享同一堆。
 * 数据以值拷贝方式跨边界传递，必须经 Ctk_FreeBuffer 释放，不得由外部分配器回收。
 */
#include "ctk_api.h"

#include <cstdlib>
#include <cstring>

CTK_API CtkError Ctk_AllocBuffer(const void* src, int32_t count,
                                 int32_t elem_size, CtkBuffer* out)
{
    if (out == 0) {
        return CTK_ERR_BAD_ARG;
    }
    out->data = 0;
    out->count = 0;
    out->elem_size = elem_size;

    if (count < 0 || elem_size < 0) {
        return CTK_ERR_BAD_ARG;
    }
    if (count == 0 || elem_size == 0) {
        /* 空 buffer 合法：data 为 NULL，count 为零 */
        out->count = count;
        return CTK_OK;
    }

    /* 防止 count * elem_size 乘法溢出 */
    size_t total = (size_t)count * (size_t)elem_size;
    if (total / (size_t)elem_size != (size_t)count) {
        return CTK_ERR_BAD_ARG;
    }

    void* block = std::malloc(total);
    if (block == 0) {
        return CTK_ERR_OUT_OF_MEM;
    }

    if (src != 0) {
        std::memcpy(block, src, total);
    } else {
        std::memset(block, 0, total);
    }

    out->data = block;
    out->count = count;
    out->elem_size = elem_size;
    return CTK_OK;
}

CTK_API void Ctk_FreeBuffer(CtkBuffer* b)
{
    if (b == 0) {
        return;
    }
    if (b->data != 0) {
        std::free(b->data);
    }
    b->data = 0;
    b->count = 0;
    b->elem_size = 0;
}
