/*
 * ctk_handle.cpp - L1 自有块释放分发（防线核心）。
 *
 * CreoToolkit_Free 是所有 native 自有资源释放的唯一入口，按 CtkOwnedBlock::kind 分发：
 *
 *   - 平坦类型（PROARRAY / WSTRING / SELECTION / ELEMENT / REFERENCE）：
 *       释放 payload 块，再释放描述符。
 *   - CTK_OWNER_PROARRAY_OF_HANDLE：
 *       ptr 为 count 个 void* 槽的数组。先对每个非 NULL 内槽调用 elem_free
 *       恰好一次，再释放外层数组，最后释放描述符。
 *       确保调用方只持有外层容器时内层元素也不泄漏。
 */
#include "ctk_api.h"

#include <cstdlib>
#include <cstring>

CTK_API CtkHandle Ctk_AllocOwnedArrayOfHandle(int32_t count, CtkElemFree elem_free)
{
    if (count < 0) {
        return 0;
    }

    CtkOwnedBlock* blk = (CtkOwnedBlock*)std::malloc(sizeof(CtkOwnedBlock));
    if (blk == 0) {
        return 0;
    }
    blk->kind = CTK_OWNER_PROARRAY_OF_HANDLE;
    blk->ptr = 0;
    blk->count = count;
    blk->elem_size = (int32_t)sizeof(void*);
    blk->elem_free = elem_free;

    if (count > 0) {
        size_t total = (size_t)count * sizeof(void*);
        void** slots = (void**)std::malloc(total);
        if (slots == 0) {
            std::free(blk);
            return 0;
        }
        std::memset(slots, 0, total);
        blk->ptr = slots;
    }
    return blk;
}

CTK_API void CreoToolkit_Free(CtkHandle h)
{
    if (h == 0) {
        return;
    }

    switch (h->kind) {
    case CTK_OWNER_PROARRAY_OF_HANDLE: {
        /* 先逐一释放内槽（恰好一次），再释放外层数组。 */
        if (h->ptr != 0) {
            void** slots = (void**)h->ptr;
            if (h->elem_free != 0) {
                for (int32_t i = 0; i < h->count; ++i) {
                    if (slots[i] != 0) {
                        h->elem_free(slots[i]);
                        slots[i] = 0;
                    }
                }
            }
            std::free(h->ptr);
            h->ptr = 0;
        }
        break;
    }

    case CTK_OWNER_PROARRAY:
    case CTK_OWNER_WSTRING:
    case CTK_OWNER_SELECTION:
    case CTK_OWNER_ELEMENT:
    case CTK_OWNER_REFERENCE:
    default: {
        /* elem_free 非空则由外部释放器（如 ProSelectionFree）释放；否则 CRT 平坦块直接 std::free。 */
        if (h->ptr != 0) {
            if (h->elem_free != 0) {
                h->elem_free(h->ptr);
            } else {
                std::free(h->ptr);
            }
            h->ptr = 0;
        }
        break;
    }
    }

    /* 最后释放描述符本身。 */
    std::free(h);
}
