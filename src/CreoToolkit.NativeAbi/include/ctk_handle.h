/*
 * ctk_handle.h - L1 owned 块描述符及释放分发契约。
 *
 * CtkOwnedBlock 记录 native 资源的释放方式；CreoToolkit_Free 按 kind 分发。
 * CTK_OWNER_PROARRAY_OF_HANDLE 类型：先逐元素经 elem_free 释放内部指针，
 * 再释放外层数组及描述符本身（嵌套所有权）。
 */
#ifndef CTK_HANDLE_H
#define CTK_HANDLE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef enum CtkOwnerKind {
    CTK_OWNER_PROARRAY = 0,          /* 平坦 ProArray，只释放外层块 */
    CTK_OWNER_WSTRING,               /* 宽字符串缓冲 */
    CTK_OWNER_SELECTION,             /* 选择句柄 */
    CTK_OWNER_PROARRAY_OF_HANDLE,    /* 内部句柄数组，逐元素先释放 */
    CTK_OWNER_ELEMENT,               /* 元素树节点 */
    CTK_OWNER_REFERENCE              /* 引用句柄 */
} CtkOwnerKind;

/* 释放单个内部元素指针；供 CTK_OWNER_PROARRAY_OF_HANDLE 逐元素调用。平坦类型可为 NULL。 */
typedef void (*CtkElemFree)(void* elem);

typedef struct CtkOwnedBlock {
    CtkOwnerKind kind;
    void*        ptr;        /* 持有的 CRT 块 */
    int32_t      count;      /* 元素数量（数组类型有效） */
    int32_t      elem_size;  /* 单个元素字节数 */
    CtkElemFree  elem_free;  /* 逐元素释放函数；平坦类型为 NULL */
} CtkOwnedBlock;

typedef CtkOwnedBlock* CtkHandle;

#ifdef __cplusplus
}
#endif

#endif /* CTK_HANDLE_H */
