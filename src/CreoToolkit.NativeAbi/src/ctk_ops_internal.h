/*
 * ctk_ops_internal.h — L1 域拆分共享内部头
 * 含所有 Pro* include、跨域 helper、跨域 struct。
 * 各 ctk_ops_*.cpp 只需 #include "ctk_ops_internal.h"。
 */
#ifndef CTK_OPS_INTERNAL_H
#define CTK_OPS_INTERNAL_H

#include "ctk_ops.h"
#include "ctk_api.h"

#include <ProToolkit.h>
#include <ProMdl.h>
#include <ProModelitem.h>
#include <ProParameter.h>
#include <ProParamval.h>
#include <ProFeature.h>
#include <ProSolid.h>
#include <ProElement.h>
#include <ProElempath.h>
#include <ProSelection.h>
#include <ProObjects.h>
#include <ProUtil.h>
#include <ProWindows.h>
#include <ProLayer.h>
#include <ProModelitem.h>
#include <ProVerstamp.h>
#include <ProPart.h>
#include <ProMaterial.h>
#include <ProAssembly.h>
#include <ProSkeleton.h>
#include <ProDrawing.h>
#include <ProAsmcomp.h>
#include <ProFeatType.h>
#include <ProFaminstance.h>
#include <ProSimprep.h>
#include <ProGtol.h>
#include <ProAxis.h>
#include <ProCsys.h>
#include <ProSurface.h>
#include <ProQuilt.h>
#include <ProDimension.h>
#include <ProRelSet.h>
#include <ProDwgtable.h>
#include <ProDtlitem.h>
#include <ProDtlnote.h>

#include <cstddef>
#include <cstdlib>
#include <cstring>
#include <cwchar>
#include <vector>

/* CtkElemtreeOwned 已物删:
 * 释放上下文平移到 .NET CreoElemtreeHandle unmanaged blob。 */

/* ---- 跨域 helper 函数（static inline，C++17） ---- */

static inline void clear_buffer(CtkBuffer* b)
{
    if (b != 0) {
        b->data = 0;
        b->count = 0;
        b->elem_size = 0;
    }
}

/* clear_param 已随 Ctk_ParameterFind/List/Set 物删。 */

static inline int32_t bad_arg(void)
{
    return CTK_ERR_BAD_ARG;
}

static inline int32_t oom(void)
{
    return CTK_ERR_OUT_OF_MEM;
}

static inline void copy_wstr(wchar_t* dst, int32_t cap, const wchar_t* src)
{
    if (dst == 0 || cap <= 0) {
        return;
    }
    dst[0] = L'\0';
    if (src == 0) {
        return;
    }
    std::wcsncpy(dst, src, (size_t)cap - 1);
    dst[cap - 1] = L'\0';
}

static inline void copy_model_name(ProFamilyMdlName dst, const wchar_t* src)
{
    copy_wstr(dst, PRO_FAMILY_MDLNAME_SIZE, src);
}

static inline ProError resolve_model(const wchar_t* name, int32_t type, ProMdl* out)
{
    if (out == 0) {
        return PRO_TK_BAD_INPUTS;
    }
    *out = 0;
    if (name == 0 || name[0] == L'\0') {
        return PRO_TK_BAD_INPUTS;
    }

    ProFamilyMdlName mdl_name;
    copy_model_name(mdl_name, name);
    ProError err = ProMdlnameInit(mdl_name, (ProMdlfileType)type, out);
    return err;
}

static inline bool is_not_found(ProError err)
{
    return err == PRO_TK_E_NOT_FOUND || err == PRO_TK_NOT_EXIST;
}

static inline bool is_solid_type(ProMdlType type)
{
    return type == PRO_MDL_PART || type == PRO_MDL_ASSEMBLY;
}

static inline bool is_layer_display_status(int32_t status)
{
    return status == (int32_t)PRO_LAYER_TYPE_NONE ||
           status == (int32_t)PRO_LAYER_TYPE_NORMAL ||
           status == (int32_t)PRO_LAYER_TYPE_DISPLAY ||
           status == (int32_t)PRO_LAYER_TYPE_BLANK ||
           status == (int32_t)PRO_LAYER_TYPE_HIDDEN ||
           status == (int32_t)PRO_LAYER_TYPE_SKIP;
}

static inline int32_t model_to_owner(ProMdl mdl, ProModelitem* owner)
{
    ProError err = ProMdlToModelitem(mdl, owner);
    return (int32_t)err;
}

/* ctk_kind_from_pro / pro_kind_from_ctk / record_from_param / pro_value_from_record /
 * ParamListState / prefix_match / param_list_action
 * 已随 Ctk_ParameterFind/List/Set 物删:
 * SDK 走 Pro* 直绑(TryResolveMdl + MakeModelOwner + ProParameterInit
 * + ProParameterValueGet/Set/Create/Visit)。 */

#endif /* CTK_OPS_INTERNAL_H */
