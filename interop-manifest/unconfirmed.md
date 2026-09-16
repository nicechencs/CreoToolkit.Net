# 所有权未确认清单 (unconfirmed)
> 对应早期设计中 free-dispatch 第 4 条与所有权治理：所有 `confirmed:false` 与所有 `mode:borrowed/live` 项，均需人工复核或泄漏压力测试兜底验证。判定依据见下。

- 登记 producer 函数: **189**（含 4 项 confirmed:true）
- 三态分布(按出参计): data=**187**(含 2 confirmed:true), live=**21**(含 1 confirmed:true 的 ProFeatureElemtreeExtract), borrowed=**1**(=confirmed:true 的 ProSelect)
- confirmed:true=**4**, confirmed:false=**185**
- `owned:false`(子项借用，禁自动 free): **2** 项（结论 1，见 §0 高危区）
- `array_free` 出参: **18** 项；`element_free` 残留: **0**（结论 2，已统一为 array_free 防双重释放）

## 0. 高危必核实 — 容器内部子项 getter（owned:false，禁自动 free；F12 崩溃风险）
> 结论 1：见 interop-review-notes.md §1。这类函数返回的是**父对象 elemtree 内部已有子项**的指针（非新分配 producer），且 `ProElement` **无数组级 free**（free-catalog 仅 ProElementFree 标量级）。头文件对子项数组**无任何 free 说明**，仅要求调用方用 ProArrayAlloc 预分配外层 ProArray 容器。
> 处理：ownership.yml 已标 `owned:false` + `borrowed_or_unknown:true` + CAUTION 注释，L1 **绝不**对子项调 ProElementFree。真机泄漏/UAF 测试**最高优先**复核 Element 子项所有权。保持 confirmed:false，泄漏/UAF 测试兜底。

| 函数 | 出参 | 类型 | 头文件 | 结论 |
|---|---|---|---|---|
| `ProElementChildrenGet` | p_child_elems | ProElement | ProElement.h:722 | owned:false 借用父 elemtree 子项 |
| `ProElementArrayGet` | p_array_elems | ProElement | ProElement.h:936 | owned:false 借用 array-element 子项 |

> 注：`ProElementReferencesGet` / `ProElementCollectionProcess` / `ProCrvcollinstrReferencesGet` / `ProFeatureReferenceEditRefsGet` 虽名含 *ReferencesGet/取子项*，但头文件**明示** "Free this output using ProReferencearrayFree"（函数分配新 ProArray）→ 属真 producer，**保持 owned**，仅按结论 2改用 array_free。

## A. mode:live 项（默认留 owned 句柄；错判为 data 会 UAF/破坏语义 — F11）
> 判定依据: 出参 pointee ∈ {ProSelection, ProElement, ProElemtree, ProReference, ProCollection}（所有权规则 2）。
> 核实点: 确认该返回是否真要回灌 Pro\*；若实为读完即弃则降级 data。泄漏测试观察是否需 Dispose。
> free 列: 结论 2 已将所有 element_free 统一为 array_free（数组级 free 自递归元素，防 L1 双重释放）。owned:false 见 §0。

| 函数 | 出参 | 类型 | 头文件 | free / owned |
|---|---|---|---|---|
| `ProAnnotationelemQuiltreferenceSurfacesCollect` | surfaces | ProSelection | ProAnnotationElem.h | ProSelectionarrayFree |
| `ProCrvcollectionInstrRegen` | r_sel_list | ProSelection | ProCollect.h | ProSelectionarrayFree (结论 2 改) |
| `ProCrvcollectionRegenerate` | r_result_sellist | ProSelection | ProCrvcollection.h | ProSelectionarrayFree (结论 2 改) |
| `ProCrvcollinstrReferencesGet` | reference_array | ProReference | ProCrvcollection.h | ProReferencearrayFree |
| `ProCurvesCollect` | sel_list | ProSelection | ProCollect.h | ProSelectionarrayFree (结论 2 改) |
| `ProDrawingDimAttachsGet` | p_attachments_arr | ProSelection | ProDrawing.h | ProSelectionarrayFree |
| `ProElementArrayGet` | p_array_elems | ProElement | ProElement.h | **owned:false → 见 §0 高危区** |
| `ProElementChildrenGet` | p_child_elems | ProElement | ProElement.h | **owned:false → 见 §0 高危区** |
| `ProElementCollectionProcess` | reference_array | ProReference | ProElement.h | ProReferencearrayFree (producer) |
| `ProElementReferencesGet` | references | ProReference | ProElement.h | ProReferencearrayFree (结论 2 改; producer) |
| `ProFeatureReferenceEditRefsGet` | r_orig_ref_arr | ProReference | ProFeature.h | ProReferencearrayFree (结论 2 改; producer) |
| `ProGeometryAtPointFind` | p_sel_arr | ProSelection | ProGeomitem.h | ProSelectionarrayFree (结论 2 改) |
| `ProNoteAttachLeadersGet` | endpoints | ProSelection | ProNote.h | ProSelectionarrayFree |
| `ProReferencearrayToSelections` | selections | ProSelection | ProReference.h | ProSelectionarrayFree |
| `ProSelbufferSelectionsGet` | ret_buff | ProSelection | ProSelbuffer.h | ProSelectionarrayFree (结论 5：真机验证是否 static 缓冲，若下次调用重分配→改 borrowed) |
| `ProSelect` | p_sel_array | ProSelection | ProSelection.h | borrowed (confirmed:true; static, 禁 free) |
| `ProSelectionarrayToReferences` | references | ProReference | ProReference.h | ProReferencearrayFree |
| `ProSolidRayIntersectionCompute` | p_sel_arr | ProSelection | ProSolid.h | ProSelectionarrayFree (结论 2 改) |
| `ProSrfcollectionRegenerate` | r_result_sellist | ProSelection | ProSrfcollection.h | ProSelectionarrayFree (结论 2 改) |
| `ProSurfacesCollect` | sel_list | ProSelection | ProCollect.h | ProSelectionarrayFree |
| `ProSurffinishReferencesGet` | surface | ProSelection | ProSurfFinish.h | ProSelectionarrayFree |

## B. mode:borrowed 项（库 static 内存；错 free 会崩溃 — F12）
> 判定依据: 出参文档含 "static memory"/"reallocated"（所有权规则 2）。
> 核实点: 确认是否为库内部 static 缓冲；若是则逐元素 copy 出 owned 副本，原指针绝不 free。

| 函数 | 出参 | 类型 | 头文件 |
|---|---|---|---|
| (confirmed:false 中无新增 borrowed；唯一确凿 borrowed 为 confirmed:true 的 `ProSelect`) | | | |

## C. mode:data 但 confirmed:false（多数为读完即弃；核实拷贝/嵌套 free 是否完整）
> 核实点: 元素是否含嵌套指针(需 element_free)；ProArray 是否需专用 *arrayFree 而非 ProArrayFree（F7/F8）。

| 函数 | 出参 | 元素类型 | resource | 头文件 |
|---|---|---|---|---|
| `ProATBMdlnameVerify` | models_out_of_date | ProMdlFileName | ProArray | ProATB.h |
| `ProATBMdlnameVerify` | models_unlinked | ProMdlFileName | ProArray | ProATB.h |
| `ProATBMdlnameVerify` | models_old_version | ProMdlFileName | ProArray | ProATB.h |
| `ProATBVerify` | models_out_of_date | ProFileName | ProArray | ProATB.h |
| `ProATBVerify` | models_unlinked | ProFileName | ProArray | ProATB.h |
| `ProATBVerify` | models_old_version | ProFileName | ProArray | ProATB.h |
| `ProAnnotationplaneNamesGet` | names | wchar_t | ProWstring | ProAnnotation.h |
| `ProAsmcompconstraintUserdataGet` | usr_data | wchar_t | ProWstring | ProAsmcomp.h |
| `ProCablesFromLogicalGet` | p_cable_names | ProName | ProArray | ProCabling.h |
| `ProCavitylayoutModelMdlnamesGet` | repl_models | ProMdlName | ProArray | ProCavitylayout.h |
| `ProCavitylayoutModelnamesGet` | repl_models | ProName | ProArray | ProCavitylayout.h |
| `ProClcmdElemCreate` | commd_lines | wchar_t | ProWstring | ProClcmdElem.h |
| `ProClcmdElemGet` | commd_lines | wchar_t | ProWstring | ProClcmdElem.h |
| `ProClcmdElemRemove` | commd_lines | wchar_t | ProWstring | ProClcmdElem.h |
| `ProClcmdElemSet` | commd_lines | wchar_t | ProWstring | ProClcmdElem.h |
| `ProConfigoptArrayGet` | value_array | ProPath | ProArray | ProUtil.h |
| `ProConnectorsFromLogicalGet` | p_w_name | ProName | ProArray | ProCabling.h |
| `ProCurrentWorkspaceExport` | source_objects | wchar_t | ProWstring | ProWorkspace.h |
| `ProCurrentWorkspaceImport` | source_objects | wchar_t | ProWstring | ProWorkspace.h |
| `ProDimensionSymtextGet` | r_text | ProLine | ProArray | ProDimension.h |
| `ProDimensionTextGet` | p_text | ProLine | ProArray | ProDimension.h |
| `ProDimensionTextWstringsGet` | p_text | wchar_t | ProWstring | ProDimension.h |
| `ProDimensionTextWstringsSet` | text | wchar_t | ProWstring | ProDimension.h |
| `ProDwgtableCelltextGet` | lines | ProWstring | ProWstring | ProDwgtable.h |
| `ProElementHoleScrewSizeGet` | r_screw_size_name | wchar_t | ProWstring | ProHole.h |
| `ProElementHoleThreadSeriesGet` | r_thread_name | wchar_t | ProWstring | ProHole.h |
| `ProElementWstringGet` | value | wchar_t | ProWstring | ProElement.h |
| `ProFilesList` | p_file_name_array | ProPath | ProArray | ProUtil.h |
| `ProFilesList` | p_subdir_name_array | ProPath | ProArray | ProUtil.h |
| `ProGtolBottomTextGet` | below_text | wchar_t | ProWstring | ProGtol.h |
| `ProGtolCompositeGet` | values | wchar_t | ProWstring | ProGtol.h |
| `ProGtolCompositeGet` | primary | wchar_t | ProWstring | ProGtol.h |
| `ProGtolCompositeGet` | secondary | wchar_t | ProWstring | ProGtol.h |
| `ProGtolCompositeGet` | tertiary | wchar_t | ProWstring | ProGtol.h |
| `ProGtolDatumReferencesGet` | primary | wchar_t | ProWstring | ProGtol.h |
| `ProGtolDatumReferencesGet` | secondary | wchar_t | ProWstring | ProGtol.h |
| `ProGtolDatumReferencesGet` | tertiary | wchar_t | ProWstring | ProGtol.h |
| `ProGtolIndicatorsGet` | symbols | wchar_t | ProWstring | ProGtol.h |
| `ProGtolIndicatorsGet` | dfs | wchar_t | ProWstring | ProGtol.h |
| `ProGtolIndicatorsSet` | symbols | wchar_t | ProWstring | ProGtol.h |
| `ProGtolIndicatorsSet` | dfs | wchar_t | ProWstring | ProGtol.h |
| `ProGtolLeftTextGet` | left_text | wchar_t | ProWstring | ProGtol.h |
| `ProGtolNameGet` | p_name | wchar_t | ProWstring | ProGtol.h |
| `ProGtolPrefixGet` | prefix | wchar_t | ProWstring | ProGtol.h |
| `ProGtolRightTextGet` | p_text | wchar_t | ProWstring | ProGtol.h |
| `ProGtolSuffixGet` | suffix | wchar_t | ProWstring | ProGtol.h |
| `ProGtolSymbolStringGet` | value | wchar_t | ProWstring | ProGtol.h |
| `ProGtolTopTextGet` | above_text | wchar_t | ProWstring | ProGtol.h |
| `ProGtolValueStringGet` | value | wchar_t | ProWstring | ProGtol.h |
| `ProImmParamsGet` | p_param_values | ProName | ProArray | ProImm.h |
| `ProKinDragSnapshotsNamesGet` | snap_names | ProName | ProArray | ProKinDrag.h |
| `ProMaterialDescriptionGet` | p_description | wchar_t | ProWstring | ProMaterial.h |
| `ProMdlAnnotationplanesCollect` | names | wchar_t | ProWstring | ProAnnotation.h |
| `ProMdlAnnotplanesFromGalleryCollect` | names | wchar_t | ProWstring | ProAnnotation.h |
| `ProMdlFeatBackupOwnerNamesGet` | TopToOwnerModelNames | ProMdlName | ProArray | ProFeature.h |
| `ProMdlFeatBackupRefMdlNamesGet` | TopToRefModelNames | ProMdlName | ProArray | ProFeature.h |
| `ProMecherrobjMessageGet` | message | wchar_t | ProWstring | ProMechItem.h |
| `ProMenuStringsSelect` | strings | wchar_t | ProWstring | ProMenu.h |
| `ProMenuStringsSelect` | help | wchar_t | ProWstring | ProMenu.h |
| `ProMenuStringsSelect` | selected | wchar_t | ProWstring | ProMenu.h |
| `ProMoldbaseParamsGet` | p_param_values | ProName | ProArray | ProMoldbase.h |
| `ProNoteTextGet` | p_note_text | wchar_t | ProWstring | ProNote.h |
| `ProNoteTextSet` | p_note_text | wchar_t | ProWstring | ProNote.h |
| `ProNoteURLWstringGet` | r_url | wchar_t | ProWstring | ProNote.h |
| `ProParameterDescriptionGet` | description | wchar_t | ProWstring | ProParameter.h |
| `ProParameterIsEnumerated` | valid_values | ProParamvalue | ProArray | ProParameter.h |
| `ProParameterSelect` | multi_parameters | ProParameter | ProArray | ProParameter.h |
| `ProPartMaterialsGet` | p_matl_names_arr | ProName | ProArray | ProMaterial.h |
| `ProRelsetConstraintsGet` | constraints | wchar_t | ProWstring | ProRelSet.h |
| `ProRelsetRelationsGet` | p_line_array | ProWstring | ProWstring | ProRelSet.h |
| `ProRmdtImmInfoGet` | p_machine_name | wchar_t | ProWstring | ProRmdt.h |
| `ProRmdtImmInfoGet` | p_param_values | wchar_t | ProWstring | ProRmdt.h |
| `ProRmdtMaterialInfoGet` | p_param_values | wchar_t | ProWstring | ProRmdt.h |
| `ProRmdtMoldBaseInfoGet` | p_param_values | wchar_t | ProWstring | ProRmdt.h |
| `ProServerActiveGet` | alias | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerAliasGet` | alias | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerAliasedURLToModelName` | alias | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerAliasedURLToModelName` | model_name | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerAliasedURLToURL` | url | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerClassGet` | server_class | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerContextGet` | context | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerContextsCollect` | data | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerLocationGet` | location | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerModelNameToAliasedURL` | aliased_url | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerMultiobjectsCheckout` | files | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerMultiobjectsCheckout` | object_url | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerObjectsCheckout` | object_url | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerObjectsRemove` | model_names | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerRegister` | aliased_url | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerVersionGet` | server_version | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerWorkspaceGet` | workspace | wchar_t | ProWstring | ProWTUtils.h |
| `ProServercheckoutoptsIncludeinstancesSet` | selected_includes | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerconflictsDescriptionGet` | description | wchar_t | ProWstring | ProWTUtils.h |
| `ProServersCollect` | aliases | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerworkspacedataContextGet` | context | wchar_t | ProWstring | ProWTUtils.h |
| `ProServerworkspacedataNameGet` | name | wchar_t | ProWstring | ProWTUtils.h |
| `ProSetdatumtagAdditionalTextGet` | lbl | wchar_t | ProWstring | ProSetDatumTag.h |
| `ProSetdatumtagLabelGet` | lbl | wchar_t | ProWstring | ProSetDatumTag.h |
| `ProSolidNoteCreate` | p_note_text | wchar_t | ProWstring | ProNote.h |
| `ProSpoolsFromLogicalGet` | p_w_name | ProName | ProArray | ProCabling.h |
| `ProTextStyleFontGet` | font | wchar_t | ProWstring | ProNote.h |
| `ProTextStyleValidate` | error_message_lines | wchar_t | ProWstring | ProNote.h |
| `ProUICascadebuttonHelptextGet` | value | wchar_t | ProWstring | ProUICascadebutton.h |
| `ProUICascadebuttonTextGet` | value | wchar_t | ProWstring | ProUICascadebutton.h |
| `ProUICheckbuttonHelptextGet` | value | wchar_t | ProWstring | ProUICheckbutton.h |
| `ProUICheckbuttonTextGet` | value | wchar_t | ProWstring | ProUICheckbutton.h |
| `ProUIDialogTitleGet` | value | wchar_t | ProWstring | ProUIDialog.h |
| `ProUIDrawingareaHelptextGet` | value | wchar_t | ProWstring | ProUIDrawingarea.h |
| `ProUIDrawingareaStringsDraw` | strings | wchar_t | ProWstring | ProUIDrawingarea.h |
| `ProUIInputpanelHelptextGet` | value | wchar_t | ProWstring | ProUIInputpanel.h |
| `ProUIInputpanelValueGet` | value | wchar_t | ProWstring | ProUIInputpanel.h |
| `ProUIInputpanelWidestringGet` | value | wchar_t | ProWstring | ProUIInputpanel.h |
| `ProUILabelHelptextGet` | value | wchar_t | ProWstring | ProUILabel.h |
| `ProUILabelTextGet` | value | wchar_t | ProWstring | ProUILabel.h |
| `ProUILayoutHelptextGet` | value | wchar_t | ProWstring | ProUILayout.h |
| `ProUILayoutTextGet` | label | wchar_t | ProWstring | ProUILayout.h |
| `ProUIListColumnlabelGet` | value | wchar_t | ProWstring | ProUIList.h |
| `ProUIListHelptextGet` | value | wchar_t | ProWstring | ProUIList.h |
| `ProUIListItemhelptextGet` | values | wchar_t | ProWstring | ProUIList.h |
| `ProUIListItemhelptextSet` | itemhelptext | wchar_t | ProWstring | ProUIList.h |
| `ProUIListLabelsGet` | values | wchar_t | ProWstring | ProUIList.h |
| `ProUIListLabelsSet` | labels | wchar_t | ProWstring | ProUIList.h |
| `ProUIMenubarHelptextGet` | value | wchar_t | ProWstring | ProUIMenubar.h |
| `ProUIMenubarItemhelptextGet` | values | wchar_t | ProWstring | ProUIMenubar.h |
| `ProUIMenubarItemhelptextSet` | values | wchar_t | ProWstring | ProUIMenubar.h |
| `ProUIMenupaneTextGet` | value | wchar_t | ProWstring | ProUIMenupane.h |
| `ProUIOptionmenuHelptextGet` | value | wchar_t | ProWstring | ProUIOptionmenu.h |
| `ProUIOptionmenuItemhelptextGet` | values | wchar_t | ProWstring | ProUIOptionmenu.h |
| `ProUIOptionmenuItemhelptextSet` | itemhelptext | wchar_t | ProWstring | ProUIOptionmenu.h |
| `ProUIOptionmenuLabelsGet` | values | wchar_t | ProWstring | ProUIOptionmenu.h |
| `ProUIOptionmenuLabelsSet` | labels | wchar_t | ProWstring | ProUIOptionmenu.h |
| `ProUIOptionmenuValueGet` | value | wchar_t | ProWstring | ProUIOptionmenu.h |
| `ProUIProgressbarHelptextGet` | value | wchar_t | ProWstring | ProUIProgressbar.h |
| `ProUIPushbuttonHelptextGet` | value | wchar_t | ProWstring | ProUIPushbutton.h |
| `ProUIPushbuttonTextGet` | value | wchar_t | ProWstring | ProUIPushbutton.h |
| `ProUIRadiogroupHelptextGet` | value | wchar_t | ProWstring | ProUIRadiogroup.h |
| `ProUIRadiogroupItemhelptextGet` | values | wchar_t | ProWstring | ProUIRadiogroup.h |
| `ProUIRadiogroupItemhelptextSet` | help_lines | wchar_t | ProWstring | ProUIRadiogroup.h |
| `ProUIRadiogroupLabelsGet` | values | wchar_t | ProWstring | ProUIRadiogroup.h |
| `ProUIRadiogroupLabelsSet` | labels | wchar_t | ProWstring | ProUIRadiogroup.h |
| `ProUISliderHelptextGet` | value | wchar_t | ProWstring | ProUISlider.h |
| `ProUISpinboxHelptextGet` | value | wchar_t | ProWstring | ProUISpinbox.h |
| `ProUITabHelptextGet` | value | wchar_t | ProWstring | ProUITab.h |
| `ProUITabItemHelptextStringGet` | help_text | wchar_t | ProWstring | ProUITab.h |
| `ProUITabItemLabelGet` | label | wchar_t | ProWstring | ProUITab.h |
| `ProUITabItemhelptextGet` | values | wchar_t | ProWstring | ProUITab.h |
| `ProUITabItemhelptextSet` | itemhelptext | wchar_t | ProWstring | ProUITab.h |
| `ProUITabLabelsGet` | values | wchar_t | ProWstring | ProUITab.h |
| `ProUITabLabelsSet` | values | wchar_t | ProWstring | ProUITab.h |
| `ProUITableCellHelptextStringGet` | help_text | wchar_t | ProWstring | ProUITable.h |
| `ProUITableCellLabelGet` | label | wchar_t | ProWstring | ProUITable.h |
| `ProUITableColumnLabelGet` | label | wchar_t | ProWstring | ProUITable.h |
| `ProUITableColumnlabelsGet` | values | wchar_t | ProWstring | ProUITable.h |
| `ProUITableColumnlabelsSet` | values | wchar_t | ProWstring | ProUITable.h |
| `ProUITableHelptextGet` | value | wchar_t | ProWstring | ProUITable.h |
| `ProUITableRowLabelGet` | label | wchar_t | ProWstring | ProUITable.h |
| `ProUITableRowlabelsGet` | values | wchar_t | ProWstring | ProUITable.h |
| `ProUITableRowlabelsSet` | values | wchar_t | ProWstring | ProUITable.h |
| `ProUITextareaHelptextGet` | value | wchar_t | ProWstring | ProUITextarea.h |
| `ProUITextareaValueGet` | lines | wchar_t | ProWstring | ProUITextarea.h |
| `ProUIThumbwheelHelptextGet` | value | wchar_t | ProWstring | ProUIThumbwheel.h |
| `ProUITreeColumnTitleGet` | title | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeHelptextGet` | value | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeNodeHelptextGet` | helptext | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeNodeLabelGet` | label | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeNodeTypeAppendStringGet` | append | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeNodeTypePrefixGet` | prefix | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeTreecellinputGet` | value | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeTreenodetypeappendsGet` | values | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeTreenodetypeappendsSet` | values | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeTreenodetypehelptextsGet` | values | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeTreenodetypehelptextsSet` | values | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeTreenodetypeprefixesGet` | values | wchar_t | ProWstring | ProUITree.h |
| `ProUITreeTreenodetypeprefixesSet` | values | wchar_t | ProWstring | ProUITree.h |
| `ProUdfdataInstancenamesGet` | instance_names | ProName | ProArray | ProUdf.h |
| `ProValueArrayToWstringArray` | p_wstring_array | wchar_t | ProWstring | ProValue.h |
| `ProViewNamesGet` | view_names | ProLine | ProArray | ProView.h |
| `ProViewNamesGet` | alternate_names | ProLine | ProArray | ProView.h |
| `ProViewNamesGetRWildfire2` | view_names | ProName | ProArray | ProViewRWildfire2.h |
| `ProViewNamesGetRWildfire2` | alternate_names | ProName | ProArray | ProViewRWildfire2.h |
| `ProWindowURLGet` | url | wchar_t | ProWstring | ProWindows.h |
| `ProWstringArrayObjectAdd` | p_array | ProWstring | ProWstring | ProWstring.h |
| `ProWstringArrayObjectRemove` | p_array | ProWstring | ProWstring | ProWstring.h |
| `ProWstringArraySizeSet` | p_array | ProWstring | ProWstring | ProWstring.h |

## D. 函数参数方向 unconfirmed（按值 vs 取地址;错方向 = 内存破坏型潜伏绑定）
> 判定依据：官方样例先例矛盾或头文件签名不足以唯一决定 `param_direction`。
> 处理：ownership.yml 该函数处**不显式登记方向**（沿用 generator 默认 `ref`），
> 消费侧 Bridge 双委托（按值 / 取地址）并存,以真机 fixture 双跑验证取一致 rc + 无泄漏后销登记。

| 函数 | 参数 | 头文件签名 | 官方样例先例 | 待办 |
|---|---|---|---|---|
| `ProReferencearrayFree` | references | ProReference.h:? `ProError ProReferencearrayFree(ProReference *references)` | UtilTree.c:1007 取地址 vs UgSmtFlgWallCreate.c:631 按值 | 真机验证裁决后固化 `in`/`ref` 并登 ownership.yml |
