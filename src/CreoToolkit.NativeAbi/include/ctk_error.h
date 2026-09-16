/*
 * ctk_error.h - L1 稳定 C ABI 错误码。
 * 0 表示成功，非零表示错误。
 */
#ifndef CTK_ERROR_H
#define CTK_ERROR_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef int32_t CtkError;

#define CTK_OK              0
#define CTK_ERR_BAD_ARG     1
#define CTK_ERR_OUT_OF_MEM  2
#define CTK_ERR_INTERNAL    3
#define CTK_ERR_CALLBACK    4

#ifdef __cplusplus
}
#endif

#endif /* CTK_ERROR_H */
