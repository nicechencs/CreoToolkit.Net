/*
 * ctk_ops_misc.cpp — ABI 信息查询 + entry stubs
 */
#include "ctk_ops_internal.h"

CTK_OPS_API void Ctk_GetAbiInfo(CtkAbiInfo* out)
{
    if (out == 0) {
        return;
    }
    std::memset(out, 0, sizeof(*out));
    out->abi_info_size = (int32_t)sizeof(CtkAbiInfo);
    out->wchar_size = (int32_t)sizeof(wchar_t);
    out->pro_name_size = PRO_NAME_SIZE;
    out->pro_line_size = PRO_LINE_SIZE;
    out->param_record_size = (int32_t)sizeof(CtkParamRecord);
    out->off_name = (int32_t)offsetof(CtkParamRecord, name);
    out->off_kind = (int32_t)offsetof(CtkParamRecord, kind);
    out->off_ival = (int32_t)offsetof(CtkParamRecord, i_val);
    out->off_dval = (int32_t)offsetof(CtkParamRecord, d_val);
    out->off_sval = (int32_t)offsetof(CtkParamRecord, s_val);
    out->off_modified = (int32_t)offsetof(CtkParamRecord, modified);
    out->elemnode_size = (int32_t)sizeof(CtkElemNode);
    out->name_record_size = (int32_t)sizeof(CtkNameRecord);
}

#ifndef CTK_OPS_NO_ENTRY_STUBS
extern "C" int user_initialize()
{
    return 0;
}

extern "C" void user_terminate()
{
}
#endif
