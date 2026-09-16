# `*Free` 函数编目 (free-catalog)
> 真源: `tools/CreoToolkit.Generator/out/ir.json` (Creo4 M140) + `protoolkit/includes` 注释
> 对应早期设计中「206 个 *Free 函数」的编目、free-dispatch 分派与失败模式 F7/F8。

## 计数对照
- **实测 `*Free` 函数总数: 245**（早期设计估算称 206，实测更高，差额来自 ListFree / ArrayFree 等变体被一并计入）
- 嵌套 free（递归释放元素，F7/F8 高危）: **34**
- 命名异义/多变体资源组（同一资源有 ≥2 个 free，F8 误配高危）: **31**

## 家族分布
| 家族 | 计数 | 说明 |
|---|---|---|
| ScalarFree | 194 | 单体/结构体 free（Type 或 Type*） |
| ProarrayFree | 27 | 双指针 ProArray 嵌套数组 free（Type**，递归释放元素） |
| ArrayFree | 13 | CamelCase Array：数组 free |
| arrayFree | 11 | 小写 array：ProArray 数组 free（Type*） |

## 嵌套 free（递归释放元素 — F7/F8）
> 这些 free 会递归释放数组每个成员自身持有的内存，**绝不可用通用 `ProArrayFree` 替代**，否则元素内存泄漏。

| free 函数 | 操作类型 | 双指针 | 注释证据 |
|---|---|---|---|
| `ProAnnotationreferencearrayFree` | ProAnnotationReference | 否 | erence_array); /* Purpose: Frees all memory owned by the annotation reference array. Licensing Requirem |
| `ProArgumentProarrayFree` | ProArgument | 是 | (双指针签名推断) |
| `ProAsmcompconstraintArrayFree` | ProAsmcompconstraint | 否 | _array ); /* Purpose: Frees all underlying memory of an assembly component constraint |
| `ProAsmcompconstraintFree` | ProAsmcompconstraint | 否 | traint ); /* Purpose: Frees all underlying memory of the assembly component constraint structure. |
| `ProAsmpathProarrayFree` | ProAsmpath | 是 | (双指针签名推断) |
| `ProCableparammemberFree` | ProCableparam | 否 | of ProCableparam. Also frees ProCableparammember* inside each ProCableparam. Input Arguments: |
| `ProCableparamproarrayFree` | ProCableparam | 是 | of ProCableparam. Also frees ProCableparammember* inside each ProCableparam. Input Arguments: |
| `ProCurveDataFree` | ProCurvedata | 是 | (双指针签名推断) |
| `ProCurvedataArrayFree` | ProCurvedata | 是 | (双指针签名推断) |
| `ProCurvedataFree` | ProCurvedata | 否 | e ); /* Purpose: Frees the underlying memory of the specified curve data structure. Input Arguments: |
| `ProEdgedataFree` | ProEdgedata | 否 | ge_data ); /* Purpose: Frees all the underlying memory for the edge data structure. Input Argum |
| `ProExtRefInfoFree` | ProExtRefInfo | 是 | (双指针签名推断) |
| `ProExtdataFree` | void | 是 | (双指针签名推断) |
| `ProFeatureZoneXsecGeomArrayFree` | ProXsecGeometry | 是 | (双指针签名推断) |
| `ProGeomitemdataFree` | ProGeomitemdata | 是 | (双指针签名推断) |
| `ProGtolleadersFree` | ProGtolleader | 是 | (双指针签名推断) |
| `ProLayeritemarrayFree` | ProLayerItem | 是 | (双指针签名推断) |
| `ProPartTessellationFree` | ProSurfaceTessellationData | 是 | (双指针签名推断) |
| `ProQuiltdataFree` | ProQuiltdata | 否 | ilt_data ); /* Purpose: Frees all the underlying memory used by the quilt data structure. Input |
| `ProSelectionFree` | ProSelection | 否 | allocated selection objects (also frees each member ProSelection). Input Arguments: se |
| `ProSelectionarrayFree` | ProSelection | 否 | allocated selection objects (also frees each member ProSelection). Input Arguments: se |
| `ProSimprepdataFree` | ProSimprepdata | 是 | (双指针签名推断) |
| `ProStringarrayFree` | char | 是 | (双指针签名推断) |
| `ProStringproarrayFree` | char | 是 | (双指针签名推断) |
| `ProSurfaceDataFree` | ProSurfacedata | 是 | (双指针签名推断) |
| `ProSurfacedataFree` | ProSurfacedata | 否 | rf_data ); /* Purpose: Frees all underlying memory of the surface data structure. Input Argumen |
| `ProToolinputFree` | ProToolinputPtr | 否 | tool_input); /* Purpose: Frees all the <i>ProToolElems</i> data structures stored within |
| `ProVerstampStringFree` | char | 是 | (双指针签名推断) |
| `ProWstringArrayFree` | ProWstring | 是 | (双指针签名推断) |
| `ProWstringarrayFree` | wchar_t | 是 | (双指针签名推断) |
| `ProWstringproarrayFree` | wchar_t | 是 | (双指针签名推断) |
| `ProXmlerrorlistProarrayFree` | ProXMLErrorlist | 是 | (双指针签名推断) |
| `ProXsecGeometryArrayFree` | ProXsecGeometry | 是 | (双指针签名推断) |
| `ProZoneReferenceArrayFree` | ProZoneReferenceWithflip | 是 | (双指针签名推断) |

## 命名异义 / 多变体风险（F8 误配）
> 同一资源存在多个候选 free，调用方易错配（如该用 `*arrayFree` 却用 `ProArrayFree`）。逐组确认正确配对。

| 资源前缀 | 候选 free 函数 |
|---|---|
| `ProAnnotationreferenceset` | `ProAnnotationreferencesetFree`, `ProAnnotationreferencesetProarrayFree` |
| `ProAsmcompconstraint` | `ProAsmcompconstraintArrayFree`, `ProAsmcompconstraintFree` |
| `ProCrvcollinstr` | `ProCrvcollinstrArrayFree`, `ProCrvcollinstrFree` |
| `ProCurvedata` | `ProCurvedataArrayFree`, `ProCurvedataFree` |
| `ProElemdiagnostic` | `ProElemdiagnosticFree`, `ProElemdiagnosticProarrayFree` |
| `ProMechbeamside` | `ProMechbeamsideFree`, `ProMechbeamsideProarrayFree` |
| `ProMechdisplacementconstr` | `ProMechdisplacementconstrFree`, `ProMechdisplacementconstrProarrayFree` |
| `ProMechdisplacementregularconstr` | `ProMechdisplacementregularconstrFree`, `ProMechdisplacementregularconstrProarrayFree` |
| `ProMechgeomref` | `ProMechgeomrefFree`, `ProMechgeomrefProarrayFree` |
| `ProMechinterpolationpnt` | `ProMechinterpolationpntFree`, `ProMechinterpolationpntProarrayFree` |
| `ProMechobjectref` | `ProMechobjectrefFree`, `ProMechobjectrefProarrayFree` |
| `ProMechshlproplamlayuplayer` | `ProMechshlproplamlayuplayerFree`, `ProMechshlproplamlayuplayerProarrayFree` |
| `ProMechstresscalcdata` | `ProMechstresscalcdataFree`, `ProMechstresscalcdataProarrayFree` |
| `ProMechtablentry` | `ProMechtablentryFree`, `ProMechtablentryProarrayFree` |
| `ProMechvalue` | `ProMechvalueFree`, `ProMechvalueProarrayFree` |
| `ProMechweldedge` | `ProMechweldedgeFree`, `ProMechweldedgeProarrayFree` |
| `ProParamtableset` | `ProParamtablesetFree`, `ProParamtablesetProarrayFree` |
| `ProReference` | `ProReferenceFree`, `ProReferencearrayFree` |
| `ProSelection` | `ProSelectionFree`, `ProSelectionarrayFree` |
| `ProServerworkspacedata` | `ProServerworkspacedataFree`, `ProServerworkspacedataProarrayFree` |
| `ProSrfcollinstr` | `ProSrfcollinstrArrayFree`, `ProSrfcollinstrFree` |
| `ProSrfcollref` | `ProSrfcollrefArrayFree`, `ProSrfcollrefFree` |
| `ProString` | `ProStringFree`, `ProStringarrayFree` |
| `ProUdfextsymbol` | `ProUdfextsymbolFree`, `ProUdfextsymbolProarrayFree` |
| `ProUdfrequiredref` | `ProUdfrequiredrefFree`, `ProUdfrequiredrefProarrayFree` |
| `ProUdfvardim` | `ProUdfvardimFree`, `ProUdfvardimProarrayFree` |
| `ProUdfvarparam` | `ProUdfvarparamFree`, `ProUdfvarparamProarrayFree` |
| `ProVariantref` | `ProVariantrefFree`, `ProVariantrefProarrayFree` |
| `ProWstring` | `ProWstringArrayFree`, `ProWstringFree`, `ProWstringarrayFree` |
| `ProXsecGeometry` | `ProXsecGeometryArrayFree`, `ProXsecGeometryFree` |
| `ProZoneReference` | `ProZoneReferenceArrayFree`, `ProZoneReferenceFree` |

## 重点资源 free 配对（七族）
| 资源 | scalar free | array free | 嵌套? |
|---|---|---|---|
| ProArray (不透明) | `—` | `ProArrayFree` | 否(仅外层) |
| ProWstring | `ProWstringFree` | `ProWstringArrayFree / ProWstringarrayFree / ProWstringproarrayFree` | 是(数组) |
| ProSelection | `ProSelectionFree` | `ProSelectionarrayFree` | 是(also frees each member) |
| ProElement | `ProElementFree` | `—` | 是(underlying) |
| ProReference | `ProReferenceFree` | `ProReferencearrayFree` | 是(also free each handle) |
| ProAnnotationReference | `—` | `ProAnnotationreferencearrayFree` | 是(all memory owned) |
| ProCableparam | `ProCableparammemberFree` | `ProCableparamproarrayFree` | 是(inside each) |
