/*
 * ctk_ops.h - L1 三态 producer 的正式 C ABI 导出契约。
 *
 * 实现见 ctk_ops_*.cpp（编入 Creo 链接 DLL，CMake 已全局带 /utf-8）。
 *
 * 三态在 ABI 上显形：
 *   DATA     -> CtkBuffer(定长记录数组或 wstring 字节); 只用 Ctk_FreeBuffer 释放。
 *   BORROWED -> CtkHandle(CTK_OWNER_PROARRAY_OF_HANDLE, 每元素 owned 副本); 只用 CreoToolkit_Free。
 *   LIVE     -> ProElement tree 由 SDK 直绑持有; 释放上下文在 .NET blob 中自管。
 *   三类释放函数不可混用——各自对应不同的所有权语义和底层 API。
 *
 * 【状态码不变量】所有导出返回 int32_t:
 *   == 0  成功;
 *   <  0  原样透传的 ProError(L2 经 Check.Eval(apiName, proCode) 分类三态; L1 绝不先折叠);
 *   >  0  L1 内部错误(CtkError: 1=BAD_ARG/2=OOM/3=INTERNAL)。
 *   Pro 码<=0、Ctk 码>0 不碰撞。
 * 【失败清零规则】任何导出失败时, 其 out 句柄/缓冲一律置 null/空(count=0),
 *   避免半初始化资源被 C# 侧误释放。
 * 【线程规则】Ctk_FreeBuffer 是纯 CRT 操作, 任意线程可调用; 所有触达 Creo 的释放
 *   (CreoToolkit_Free 经 elem_free 调 Pro*Free, Elemtree 经 SDK DirectReleaser)须在 Creo 主线程执行,
 *   由 L2/L3 的 CreoDispatcher 保证调度。
 *
 * 原 30+ 个 Ctk_* wrapper 随 generator 解锁逐批退役为 Pro* 直绑并物删；
 * 本头当前仅保留 ABI 元数据(Ctk_GetAbiInfo + 三个定长记录)。
 * 准入门 G1–G6 详见架构方案。
 */
#ifndef CTK_OPS_H
#define CTK_OPS_H

#include <stdint.h>
#include "ctk_buffer.h"   /* CtkBuffer */
#include "ctk_handle.h"   /* CtkHandle / CtkOwnedBlock */
#include "ProSizeConst.h" /* PRO_NAME_SIZE / PRO_LINE_SIZE (Creo 定长常量) */

#ifdef __cplusplus
#  define CTK_OPS_API extern "C" __declspec(dllexport)
#else
#  define CTK_OPS_API __declspec(dllexport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* 参数值类型(与 C# CreoParamValueKind 对齐, 但不含 Unset; 这里是 wire 值)。 */
#define CTK_PARAM_STRING 1
#define CTK_PARAM_DOUBLE 2
#define CTK_PARAM_INT    3
#define CTK_PARAM_BOOL   4

/*
 * CtkParamRecord - 参数的定长 DATA 记录(copy-out 后无 native 所有权)。全定长 → 可放进 CtkBuffer
 * 数组、C# 用 [StructLayout] 镜像。kind 决定哪个值字段有效。name/s_val 是 Creo 定长宽串(含 NUL)。
 */
typedef struct CtkParamRecord {
    wchar_t name[PRO_NAME_SIZE];   /* 参数名(定长, 含 NUL)            */
    int32_t kind;                  /* CTK_PARAM_*                      */
    int32_t i_val;                 /* Int; Bool 用 0/1                 */
    double  d_val;                 /* Double                           */
    wchar_t s_val[PRO_LINE_SIZE];  /* String(定长 ProLine, 含 NUL)     */
    int32_t modified;              /* ProParameterIsModified 结果      */
} CtkParamRecord;

/* CtkElemNode - 元素树节点的定长 DATA 摘要(抽取时一次性 copy-out)。 */
typedef struct CtkElemNode {
    int32_t element_id;            /* ProElementIdGet                  */
    int32_t level;                 /* 遍历层级(根=0)                  */
} CtkElemNode;

/* CtkNameRecord - ProName 定长 DATA 记录，用于只读名称快照。 */
typedef struct CtkNameRecord {
    wchar_t name[PRO_NAME_SIZE];
} CtkNameRecord;

/*
 * CtkAbiInfo - ABI 不变量自描述。C# 启动/测试时 Ctk_GetAbiInfo 取回，与托管镜像
 * 逐项断言（sizeof/offsetof/常量），不一致 fail fast——比 [StructLayout] 注释可靠。
 */
typedef struct CtkAbiInfo {
    int32_t abi_info_size;         /* sizeof(CtkAbiInfo) 自校验        */
    int32_t wchar_size;            /* sizeof(wchar_t) (Windows=2)      */
    int32_t pro_name_size;         /* PRO_NAME_SIZE                    */
    int32_t pro_line_size;         /* PRO_LINE_SIZE                    */
    int32_t param_record_size;     /* sizeof(CtkParamRecord)           */
    int32_t off_name;              /* offsetof(CtkParamRecord, name)   */
    int32_t off_kind;              /* offsetof(.., kind)               */
    int32_t off_ival;              /* offsetof(.., i_val)              */
    int32_t off_dval;              /* offsetof(.., d_val)              */
    int32_t off_sval;              /* offsetof(.., s_val)              */
    int32_t off_modified;          /* offsetof(.., modified)           */
    int32_t elemnode_size;         /* sizeof(CtkElemNode)              */
    int32_t name_record_size;      /* sizeof(CtkNameRecord)            */
} CtkAbiInfo;

/* 填 ABI 不变量(纯值, 无 Creo 调用, 任意线程可调)。out 不为空。 */
CTK_OPS_API void Ctk_GetAbiInfo(CtkAbiInfo* out);

#ifdef __cplusplus
}
#endif

#endif /* CTK_OPS_H */
