using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Events;

namespace CreoToolkit.Sdk.Native;

/// <summary>
/// L3↔L2 的 native 接缝。L3 对象模型只依赖本接口,
/// 不直接碰 P/Invoke / SafeHandle / IntPtr——native 边界由实现侧封死。
/// <para>
/// 三态在返回类型上显形: DATA 方法返回纯托管值(native 内存已 copy-out-free-in);
/// LIVE/BORROWED 方法返回 <see cref="INativeResource"/>(owned 句柄/副本集), 由 L3 在作用域结束时
/// 经 dispatcher 释放。所有实现都假定调用方已在 Creo 主线程(由 L3 经 CreoDispatcher 保证)。
/// </para>
/// 真实实现 <c>CreoNativeBridge</c> 包 L2; 测试用 fake 实现, 从而 L3 可脱 Creo 单测。
/// </summary>
internal interface ICreoNative
{
    // ---- 配置项 (DATA) ----
    /// <summary>读取 Creo 配置项值(ProConfigoptionGet)；选项不存在返回 null。</summary>
    string? ConfigOptionGet(string option);
    /// <summary>读取多值配置项的全部值(ProConfigoptArrayGet，如 search_path)；选项不存在返回空列表。</summary>
    IReadOnlyList<string> ConfigOptionArrayGet(string option);
    /// <summary>设置 Creo 配置项值(ProConfigoptSet)。多值项(如 search_path)为追加新条目。</summary>
    void ConfigOptionSet(string option, string value);

    // ---- 窗口 (DATA) ----
    /// <summary>当前窗口 id；无可用窗口 id(message area 等)返回 null。</summary>
    int? WindowCurrentId();
    /// <summary>重绘指定窗口；调用方负责提供有效窗口 id。</summary>
    void WindowRepaint(int windowId);
    /// <summary>尝试重绘指定窗口；当前上下文不可重绘时返回 false。</summary>
    bool WindowTryRepaint(int windowId);
    /// <summary>为已在会话中的模型创建/取回窗口并激活(ProObjectwindowMdlnameCreate + ProWindowActivate),
    /// 使 ProMdlCurrentGet 返回该模型。返回窗口 id。</summary>
    int ModelWindowOpen(ModelIdentity model);

    // ---- 模型 (DATA / 借用会话句柄) ----
    /// <summary>当前模型标识(名 + 类型); 无当前模型返回 null。名是 wstring copy-out 的 DATA。</summary>
    ModelIdentity? ModelCurrent();
    /// <summary>判断指定 (名, 类型) 的模型是否在会话中(供 Retrieve 校验)。</summary>
    bool ModelExists(string name, CreoModelType type);
    /// <summary>按名字经 search_path 检索/加载模型(ProMdlnameRetrieve); 找不到返回 null。</summary>
    ModelIdentity? ModelRetrieveByName(string name, CreoModelType type);
    /// <summary>重生成模型(动作型, 失败抛 CreoException)。</summary>
    void ModelRegenerate(ModelIdentity model);
    /// <summary>保存模型(动作型)。</summary>
    void ModelSave(ModelIdentity model);
    /// <summary>显示模型(动作型;真样板:production 走生成绑定 `ProMdlnameRetrieve+ProMdlDisplay` 经 <see cref="IGeneratedInvoker"/>;fake 路径 in-memory 记录)。</summary>
    void ModelDisplay(ModelIdentity model);
    /// <summary>列出模型首层依赖(DATA 快照,只 name;type/extension 留 future)。无依赖返回空列表,不视为错误。</summary>
    IReadOnlyList<string> ModelDependenciesList(ModelIdentity model);
    /// <summary>查询模型是否被 Creo 标记为 modified。</summary>
    bool ModelIsModified(ModelIdentity model);
    /// <summary>从会话内存擦除模型(动作型); 模型不在会话返回 false。</summary>
    bool ModelErase(ModelIdentity model);
    /// <summary>复制模型为新名(会话内存副本); 源不在会话返回 false。</summary>
    bool ModelCopy(ModelIdentity model, string newName);
    /// <summary>读取模型磁盘来源路径(DATA); 无来源返回 null。</summary>
    string? ModelOrigin(ModelIdentity model);
    /// <summary>读取模型 common name(DATA); 无则返回 null。</summary>
    string? ModelCommonName(ModelIdentity model);
    /// <summary>ProMdlMdlnameGet: 读取模型内部名(DATA); 不可读返回 null。</summary>
    string? ModelMdlnameGet(ModelIdentity model);
    /// <summary>ProMdlDisplaynameGet: 读取模型显示名(DATA); 不可读返回 null。</summary>
    string? ModelDisplaynameGet(ModelIdentity model);
    /// <summary>ProMdlDirectoryPathGet: 读取模型目录路径(DATA); 不可读返回 null。</summary>
    string? ModelDirectoryPathGet(ModelIdentity model);
    /// <summary>ProMdlSubtypeGet: 读取模型 subtype int; 不可读返回 null。</summary>
    int? ModelSubtypeGet(ModelIdentity model);
    /// <summary>ProMdlFiletypeGet: 读取模型 filetype int; 不可读返回 null。</summary>
    int? ModelFiletypeGet(ModelIdentity model);
    /// <summary>ProMdlIdGet: 读取模型数字 id; 不可读返回 null。</summary>
    int? ModelIdGet(ModelIdentity model);
    /// <summary>ProVerstampStringGet: 读取模型版本戳字符串(DATA); 不可读返回 null。</summary>
    string? ModelVerstampGet(ModelIdentity model);
    /// <summary>ProMdlObjectDefaultnameGet: 按对象类型查询默认名(不依赖模型实例); 不可读返回 null。</summary>
    string? ObjectDefaultNameGet(CreoModelItemType objectType);
    /// <summary>取实体模型的再生包围盒(DATA)。仅 Part/Assembly 有效。</summary>
    CreoBoundingBox ModelOutlineGet(ModelIdentity model);
    /// <summary>取实体模型质量属性(DATA,v1:7 标量)。仅 Part/Assembly 有效。</summary>
    CreoMassProperty ModelMassPropertyGet(ModelIdentity model);
    /// <summary>取实体模型在指定坐标系下的质量属性(DATA,v1:7 标量)。仅 Part/Assembly 有效。</summary>
    CreoMassProperty ModelMassPropertyGet(ModelIdentity model, string coordinateSystemName);

    // ---- 图层 (DATA) ----
    /// <summary>列出模型 layer 名称快照。</summary>
    IReadOnlyList<string> LayerNamesList(ModelIdentity model);
    /// <summary>枚举 model 所有 layer,返 (model, Layer, id) ItemRef 列表(供 CreoModelItems.List(Layer) / CreoLayers.List)。</summary>
    IReadOnlyList<ItemRef> ModelLayerList(ModelIdentity model);
    /// <summary>枚举 surface 上所有 edge(ProSurfaceContourVisit + ProContourEdgeVisit 嵌套),
    /// 返 (surface.Model, Edge, id) 去重 ItemRef 列表。owner 不可解析 / surface 不存在 / 无 edge 返空列表。</summary>
    IReadOnlyList<ItemRef> SurfaceEdgeList(ItemRef surface);
    /// <summary>枚举 solid 中所有 curve(datum curve 等):遍历 feature + PRO_CURVE geomitem visit,去重返 ItemRef 列表。
    /// 非 Solid / owner 不可解析 → 空列表。</summary>
    IReadOnlyList<ItemRef> SolidCurveList(ModelIdentity model);
    /// <summary>创建模型 layer；已存在返回 false。</summary>
    bool LayerCreate(ModelIdentity model, string name);
    /// <summary>删除模型 layer；不存在返回 false。</summary>
    bool LayerDelete(ModelIdentity model, string name);
    /// <summary>读取 layer 临时显示状态；不存在返回 null。</summary>
    CreoLayerDisplayStatus? LayerDisplayStatusGet(ModelIdentity model, string name);
    /// <summary>设置 layer 临时显示状态；不存在返回 false。</summary>
    bool LayerDisplayStatusSet(ModelIdentity model, string name, CreoLayerDisplayStatus status);
    /// <summary>将 feature 加入 layer；layer 不存在或已包含时返回 false。</summary>
    bool LayerFeatureAdd(ModelIdentity model, string name, ItemRef feature);
    /// <summary>查询 layer 是否包含 feature；layer 不存在返回 false。</summary>
    bool LayerFeatureContains(ModelIdentity model, string name, ItemRef feature);

    // ---- 参数 (DATA: copy-out-free-in 即时快照) ----
    /// <summary>
    /// 列出参数的值快照。<paramref name="query"/> 非空时为**过滤下推点**: L1 支持则下推, 否则实现侧
    /// 取全量再 <see cref="ParameterQuery.Matches"/> 过滤——避免在大模型上把上万参数全拉回 L3 再筛。
    /// </summary>
    IReadOnlyList<CreoParameter> ParameterList(ModelIdentity model, ParameterQuery? query);
    /// <summary>分页列出参数(大模型上万参数用)。</summary>
    IReadOnlyList<CreoParameter> ParameterListPage(ModelIdentity model, int offset, int limit);
    /// <summary>直查单个参数; NOT_FOUND 返回 null。</summary>
    CreoParameter? ParameterFind(ModelIdentity model, string name);
    /// <summary>设置参数值(动作型)。</summary>
    void ParameterSet(ModelIdentity model, string name, CreoParamValue value);
    /// <summary>删除参数; 不存在返回 false。</summary>
    bool ParameterDelete(ModelIdentity model, string name);
    /// <summary>重置参数到上次设置前的值; 不存在返回 false。</summary>
    bool ParameterReset(ModelIdentity model, string name);
    /// <summary>读取参数 designation 状态; 参数不存在返回 null(区别于"存在但未 designated"=false)。</summary>
    bool? ParameterIsDesignated(ModelIdentity model, string name);
    /// <summary>设置参数 designation 状态; 不存在返回 false。</summary>
    bool ParameterSetDesignated(ModelIdentity model, string name, bool designated);
    /// <summary>读取参数 description(DATA wstring,ProParameterDescriptionGet 直绑);
    /// 参数不存在或无 description 返回 null。首例样板。</summary>
    string? ParameterDescription(ModelIdentity model, string name);
    /// <summary>读取带单位参数值快照; 参数不存在返回 null,参数无单位时 Unit 为 null。</summary>
    CreoParameterValueWithUnits? ParameterGetWithUnits(ModelIdentity model, string name);
    /// <summary>设置带单位 double 参数值; unit 为 null 时按 owner 模型单位写入。</summary>
    void ParameterSetWithUnits(ModelIdentity model, string name, double value, CreoUnit? unit);
    /// <summary>读取参数自身单位; 参数不存在或无单位返回 null。</summary>
    CreoUnit? ParameterUnitsGet(ModelIdentity model, string name);
    /// <summary>重设参数自身单位; 参数不存在或无单位返回 false。</summary>
    bool ParameterUnitsAssign(ModelIdentity model, string name, CreoUnit unit);

    // ---- 单位 (DATA) ----
    /// <summary>按单位名初始化模型内单位快照; 不存在返回 null。</summary>
    CreoUnit? UnitInit(ModelIdentity model, string unitName);
    /// <summary>按单位表达式初始化模型内单位快照; 表达式无效返回 null。</summary>
    CreoUnit? UnitFromExpression(ModelIdentity model, string expression);
    /// <summary>计算 fromName 到 toName 的线性单位换算参数。</summary>
    CreoUnitConversion UnitConvert(ModelIdentity model, string fromName, string toName);
    /// <summary>读取单位表达式; 单位不存在返回空字符串。</summary>
    string UnitExpressionGet(ModelIdentity model, string unitName);
    /// <summary>读取模型主单位系统快照; 模型类型不支持时返回 null。</summary>
    CreoUnitSystem? ModelPrincipalUnitSystemGet(ModelIdentity model);
    /// <summary>列出模型所有单位系统快照。</summary>
    IReadOnlyList<CreoUnitSystem> ModelUnitSystemsList(ModelIdentity model);
    /// <summary>设模型主单位系统(动作型,触发再生)。</summary>
    void ModelPrincipalUnitSystemSet(
        ModelIdentity model, CreoUnitSystem newSystem,
        CreoToolkit.Sdk.Units.CreoUnitConversionMode conversionMode, bool ignoreParamUnits,
        int regenerationFlags = -1);

    /// <summary>读取 Note 的 URL(DATA wstring,ProNoteURLWstringGet 直绑);
    /// 模型不可解析、note 项无 URL 或不可读返回 null。直绑,
    /// 验证模板在 ProModelitem 派生项(POD struct 入参)上批量可复制。</summary>
    string? NoteUrlGet(ItemRef note);

    // ---- 模型项 (DATA) ----
    /// <summary>取模型项名字(经 ProModelitemNameGet, DATA copy-out); 无名或不可读返回 null。</summary>
    string? ModelitemNameGet(ItemRef item);
    /// <summary>改名(ProModelitemNameSet,动作型,失败抛)。</summary>
    void ModelitemNameSet(ItemRef item, string name);
    /// <summary>查询是否可改名(ProModelitemNameCanChange);不可读返回 null。</summary>
    bool? ModelitemNameCanChange(ItemRef item);
    /// <summary>取默认名(ProModelitemDefaultnameGet, DATA copy-out);不可读返回 null。</summary>
    string? ModelitemDefaultnameGet(ItemRef item);
    /// <summary>删除用户名(ProModelitemUsernameDelete,动作型,失败抛)。</summary>
    void ModelitemUsernameDelete(ItemRef item);

    /// <summary>取面类型 int(ProSurfaceInit + ProSurfaceTypeGet);不可读返回 null。
    /// 返 int 对应 pro_srf_type enum 值:PRO_SRF_PLANE=34/CYL=36/CONE=37/TORUS=38/COONS=39/SPL=40/FIL=41/RUL=42/REV=43/TABCYL=44/B_SPL=45/FOREIGN=46/CYL_SPL=48/SPL2DER=50。</summary>
    int? SurfaceTypeGet(ItemRef surface);

    /// <summary>取基准点 3D 坐标(ProPointInit + ProPointCoordGet, DATA copy-out);
    /// owner 模型不可解析或点 id 不存在返回 null。返 3-double = (x,y,z) 在 owner 模型坐标系下。</summary>
    double[]? PointCoordGet(ItemRef point);

    /// <summary>取坐标系 4×4 变换矩阵(ProCsysInit + ProCsysDataGet, DATA copy-out + row-major 装填);
    /// owner 不可解析或 csys id 不存在返回 null。返 16-double 行优先矩阵, 跟 <c>CreoTransform</c> 约定一致。</summary>
    double[]? CsysMatrixGet(ItemRef csys);

    /// <summary>取 PLANE 类型面的几何数据(ProSurfaceInit + ProSurfaceTypeGet + ProSurfacedataGet + ProPlanedataGet, DATA copy-out)。
    /// 非 PLANE 类型(PRO_SRF_PLANE=34)/ owner 不可解析 / surface id 不存在均返回 null。
    /// 返 <see cref="SurfacePlaneData"/> 4 个 3-double 数组(e1/e2/e3/origin),L3 上层包成 <see cref="CreoPlane"/>(Origin=origin, Normal=e3)。</summary>
    SurfacePlaneData? SurfacePlaneDataGet(ItemRef surface);

    // ---- ProSolid 共享 API (DATA) ----
    /// <summary>取实体再生状态 int(ProSolidRegenerationStatusGet); 不可读返回 null。</summary>
    int? SolidRegenerationStatusGet(ModelIdentity model);
    /// <summary>计算实体包围盒(ProSolidOutlineCompute,基坐标系且不排除几何); 不可读返回 null。</summary>
    CreoBoundingBox? SolidOutlineCompute(ModelIdentity model);
    /// <summary>取实体精度(ProSolidAccuracyGet); 不可读返回 null。</summary>
    SolidAccuracyRecord? SolidAccuracyGet(ModelIdentity model);
    /// <summary>查询实体是否处于 noresolve 模式(ProSolidIsNoresolveMode); 不可读返回 null。</summary>
    bool? SolidIsNoresolveMode(ModelIdentity model);
    /// <summary>列出失败特征 id 列表(ProSolidFailedfeaturesList); 无失败返回空列表。</summary>
    IReadOnlyList<int> SolidFailedFeatures(ModelIdentity model);
    /// <summary>将模型自身转为 modelitem 视图(ProMdlToModelitem); 不支持返回 null。</summary>
    ItemRef? ModelToModelItem(ModelIdentity model);

    // ---- 特征 + 元素树 (LIVE: owned 句柄, 释放须带 feature 上下文) ----
    /// <summary>取模型首个可枚举特征; 无则 null。</summary>
    ItemRef? FirstFeature(ModelIdentity model);
    /// <summary>列出模型所有可枚举特征 id(DATA); 非 solid 返回空列表。</summary>
    IReadOnlyList<ItemRef> FeatureList(ModelIdentity model);
    /// <summary>ProFeatureGeomitemVisit: 枚举 feature 内指定类型 geomitem(datum point 等
    /// 无 ProSolidXxxVisit 家族的类型经此按 feature 收集);无该类项返空列表。</summary>
    IReadOnlyList<ItemRef> FeatureGeomitemsList(ItemRef feature, CreoModelItemType itemType);
    /// <summary>删除单个特征(CLIP 模式, 连带依赖子特征一起删除)。</summary>
    void FeatureDelete(ItemRef feature);
    /// <summary>抽取特征的 LIVE 元素树: 返回 owned 句柄 + 已 copy-out 的只读节点摘要。</summary>
    ElemtreeExtractResult ElemtreeExtract(ItemRef feature);
    /// <summary>深度抽取特征的 LIVE 元素树: 节点包含类型化元素值快照(ProElement*Get 读值)。</summary>
    ElemtreeExtractDeepResult ElemtreeExtractDeep(ItemRef feature);
    /// <summary>提取特征元素树并写入 XML 文件(extract → write → free 全生命周期内闭)。</summary>
    void ElemtreeWriteXml(ItemRef feature, string xmlPath);
    /// <summary>按 spec 树创建特征(自建树,Alloc→Set→Add→Create→Free 单次序列内闭,
    /// 树用 ProElementFree 释放);返回新特征引用,失败抛异常(含诊断上下文)。</summary>
    ItemRef FeatureCreateFromElemtree(ModelIdentity model, ElemSpec root);
    /// <summary>提取特征元素树,按 elemIdPath 定位元素改 double 值,经 Redefine 写回(单次序列内闭)。</summary>
    void FeatureRedefineElemDouble(ModelIdentity model, int featureId, int[] elemIdPath, double value);

    /// <summary>读取特征 status + type；特征无效返 null。</summary>
    CreoFeatureInfo? FeatureInfoGet(ItemRef feature);
    /// <summary>读取特征的父特征 id 列表；无父返空列表。</summary>
    IReadOnlyList<int> FeatureParentsGet(ItemRef feature);
    /// <summary>读取特征的子特征 id 列表；无子返空列表。</summary>
    IReadOnlyList<int> FeatureChildrenGet(ItemRef feature);

    // ---- 选择 (BORROWED: 原 static 不碰, 返回 owned 副本集) ----
    /// <summary>[Obsolete] 创建模型自身的 owned selection 副本；仅空 itemRefs 合法，
    /// 非空一律拒绝（public alias 同契约，不转发交互式 <see cref="Select"/>）。</summary>
    [Obsolete("已废弃：改用 Select(optionKeywords) 或 CreateModelItemSelection(item,path)；本 seam 仅接受空兼容列表。")]
    SelectResult SelectProgrammatic(ModelIdentity model, IReadOnlyList<string> itemRefs);

    /// <summary>ProSelect 交互式(对齐 pfcBaseSession::Select):阻塞主线程,弹 UI 让用户在 Creo 里
    /// 点选;点完 middle-click 结束返回。
    /// <para>owned-copy 出口:ProSelect 返回的数组归 BORROWED(库 static, ownership.yml
    /// element_copy 契约),逐条 ProSelectionCopy 拷出 owned 副本进 <see cref="SelectResult.Set"/>;
    /// 每项摘要含 per-item owner(<c>pro_model_item.owner</c> 反查 ModelIdentity,不可解析时 null)、
    /// raw TypeCode(Enum 未定义值不丢)与装配组件路径(ProSelectionAsmcomppathGet,无路径 null)。</para>
    /// <para>用户取消(USER_ABORT/PICK_ABOVE)或零选中返回空 SelectResult(空 owned 集,非 null)。</para>
    /// <para>optionKeywords 必须传 tkuse/otkug 官方 filter token(如
    /// <see cref="CreoSelectFilters.DraftEntity"/>),多值逗号分隔;"any" 不是有效 token——drawing
    /// 真机任何 item 都选不中(2026-07-07 验证)。maxNumSels=-1 不限数量(官方默认)。</para></summary>
    SelectResult Select(string optionKeywords, int maxNumSels);
    /// <summary>由 (item, path) 创建 selection(对齐 pfcSelect 全局工厂 CreateModelItemSelection:
    /// ProModelitemInit + ProAsmcomppathInit + ProSelectionAlloc,即刻 ProSelectionCopy 出 owned
    /// 副本)。ProSelection.h:196-214 组合表:path=null 仅限 part 场景,assembly 内组件几何必须给 path。
    /// item owner/path root 不在会话抛 <see cref="Errors.CreoException"/>。</summary>
    SelectResult CreateModelItemSelection(ItemRef item, ComponentPathData? path);
    /// <summary>高亮副本集中第 index 项(动作型, 在 set 仍存活时调用)。</summary>
    void SelectionHighlight(INativeResource set, int index);
    /// <summary>取消高亮副本集中第 index 项(动作型, 在 set 仍存活时调用)。</summary>
    void SelectionUnhighlight(INativeResource set, int index);
    /// <summary>显示副本集中第 index 项(动作型, 在 set 仍存活时调用)。</summary>
    void SelectionDisplay(INativeResource set, int index);

    // ---- Part 独有 API (DATA) ----
    /// <summary>列出零件所有材料名称(DATA wstring[])。不支持或无材料返回空列表。</summary>
    IReadOnlyList<string> PartMaterialNames(ModelIdentity model);
    /// <summary>读取零件当前材料名(DATA wstring)。未配置返回 null。</summary>
    string? PartMaterialCurrent(ModelIdentity model);
    /// <summary>读取零件密度(double)。不支持返回 null。</summary>
    double? PartDensityGet(ModelIdentity model);

    // ---- Assembly 独有 API (DATA) ----
    /// <summary>列出顶层组件特征 id(DATA int[])。无子件返回空列表。</summary>
    IReadOnlyList<int> AssemblyTopLevelComponents(ModelIdentity model);
    /// <summary>读取骨架模型名(DATA wstring)。无骨架返回 null。</summary>
    string? AssemblySkeleton(ModelIdentity model);
    /// <summary>ProAsmcomppathMdlGet: 按组件 id 路径读取子模型身份；无效路径返回 null。</summary>
    ModelIdentity? AssemblyPathMdlGet(ModelIdentity assembly, IReadOnlyList<int> compIdPath);
    /// <summary>ProAsmcomppathTrfGet: 按组件 id 路径读取 4x4 变换矩阵(16 double)；无效路径返回 null。</summary>
    double[]? AssemblyPathTransformGet(ModelIdentity assembly, IReadOnlyList<int> compIdPath, bool localToTop);

    // ---- Drawing 独有 API (DATA) ----
    /// <summary>读取图纸总页数(int)。不支持返回 null。</summary>
    int? DrawingSheetsCount(ModelIdentity model);
    /// <summary>读取当前图纸页序号(int, 1-based)。不支持返回 null。</summary>
    int? DrawingCurrentSheet(ModelIdentity model);
    /// <summary>读取视图总数(int)。不支持返回 null。</summary>
    int? DrawingViewsCount(ModelIdentity model);
    /// <summary>列出视图 id 列表(走 ProDrawingViewVisit 4 参 + ProDrawingViewIdGet 提取整型 id)。无视图返空列表。</summary>
    IReadOnlyList<int> DrawingViewIdList(ModelIdentity model);

    /// <summary>列出 drawing view DATA 快照(sheet/scale/name/solid 全收集)。
    /// 单个 view 元数据提取失败时跳过该 view,skipped 计入返回值。</summary>
    (IReadOnlyList<CreoDrawingView> Views, int Skipped) DrawingViewsList(ModelIdentity model, long sessionEpoch);
    /// <summary>列出 drawing 关联的全部 part/assembly solid；无关联实体返空列表。</summary>
    IReadOnlyList<ModelIdentity> DrawingSolidsList(ModelIdentity model);
    /// <summary>列出指定 sheet 的 detail symbol instance；sheet 由调用方传具体 1-based 页号。</summary>
    IReadOnlyList<ItemRef> DrawingDetailSymbolInstancesList(ModelIdentity model, int sheet);

    // ---- 基类新增 API ----
    /// <summary>Ctk_ModelIsSkeleton: 查询模型是否为骨架；不可读返回 null。</summary>
    bool? ModelIsSkeleton(ModelIdentity model);
    /// <summary>Ctk_ModelDependenciesCleanup: 清理模型依赖（动作型）。</summary>
    void ModelDependenciesCleanup(ModelIdentity model);

    // ---- ProSolid 新增 API ----
    /// <summary>Ctk_SolidFamtableCheck: 检查 Family Table 状态 int；不可读返回 null。</summary>
    int? SolidFamtableCheckStatus(ModelIdentity model);
    /// <summary>Ctk_SolidIsFaminstance: 查询是否为 Family Table 实例；不可读返回 null。</summary>
    bool? SolidIsFaminstance(ModelIdentity model);
    /// <summary>列出族表中所有实例名(ProFamtableInit + ProFamtableInstanceVisit)；无族表返空列表。</summary>
    IReadOnlyList<string> FamilyTableInstanceNames(ModelIdentity model);
    /// <summary>取实例对应的类属模型名(ProFaminstanceGenericGet)；非实例返 null。</summary>
    string? FamilyInstanceGenericName(ModelIdentity model);

    // ---- Assembly 写入 API ----
    /// <summary>装入组件并设置约束(ProAsmcompAssemble + ConstraintsSet)。constraints 为空时纯 packaged 装入。</summary>
    ItemRef ComponentAssemble(ModelIdentity assembly, ModelIdentity component, IReadOnlyList<AssemblyConstraintSpec> constraints);
    /// <summary>读回组件约束(ProAsmcompConstraintsWithComppathGet + 逐条 TypeGet/OffsetGet/参照)。
    /// 单条 Get 失败时字段置 null 继续(尽力语义);数组级失败抛。</summary>
    IReadOnlyList<AssemblyConstraintInfo> ComponentConstraintsRead(ModelIdentity assembly, int compFeatId);
    /// <summary>模板造件(ProAsmcompMdlnameCreateCopy); template 为 null 时造空组件。</summary>
    ItemRef ComponentCreateByCopy(ModelIdentity assembly, string compName, CreoModelType compType, ModelIdentity? template, bool leaveUnplaced);
    /// <summary>设置组件位置 + 再生(ProAsmcompPositionSet + ProAsmcompRegenerate 原子语义)。</summary>
    void ComponentPositionSet(ModelIdentity assembly, int compFeatId, double[] matrix16);

    // ---- Assembly 新增 API ----
    /// <summary>Ctk_AssemblyExplode: 展开爆炸视图（动作型）。</summary>
    void AssemblyExplode(ModelIdentity model);
    /// <summary>Ctk_AssemblyUnexplode: 收拢爆炸视图（动作型）。</summary>
    void AssemblyUnexplode(ModelIdentity model);
    /// <summary>Ctk_AssemblyIsExploded: 查询装配是否处于爆炸状态；不可读返回 null。</summary>
    bool? AssemblyIsExploded(ModelIdentity model);
    /// <summary>Ctk_AssemblySimprepRetrieve: 按名字检索简化表示（静态，不依赖 model instance）。</summary>
    void AssemblySimprepRetrieve(string assemName, CreoModelType fileType, string simpRepName);

    // ---- Drawing 新增 API ----
    /// <summary>Ctk_DrawingCurrentSolidGet: 获取图纸关联实体模型标识；无关联返回 null。</summary>
    ModelIdentity? DrawingCurrentSolid(ModelIdentity model);

    // ---- 状态查询 + 生命周期 ----
    /// <summary>查询模型是否可修改（ProMdlModifiableGet）；不可读返回 null。</summary>
    bool? ModelIsModifiable(ModelIdentity model);
    /// <summary>查询模型是否允许保存（ProMdlSaveAllowed）；不可读返回 null。</summary>
    bool? ModelIsSaveAllowed(ModelIdentity model);
    /// <summary>查询模型位置是否为标准路径（ProMdlLocationIsStandard）；不可读返回 null。</summary>
    bool? ModelLocationIsStandard(ModelIdentity model);
    /// <summary>查询模型是否被锁定（ProMdlLockGet）；不可读返回 null。</summary>
    bool? ModelLockGet(ModelIdentity model);
    /// <summary>设置模型锁定状态（ProMdlLockSet，动作型）。</summary>
    void ModelLockSet(ModelIdentity model, bool locked);
    /// <summary>备份模型到指定目录（ProMdlSave with dir，动作型）。</summary>
    void ModelBackup(ModelIdentity model, string targetDirectory);
    /// <summary>重命名模型（ProMdlnameRename，动作型）。</summary>
    void ModelRename(ModelIdentity model, string newName, bool saveAfter);
    /// <summary>删除模型文件（ProMdlDelete，动作型）。</summary>
    void ModelDelete(ModelIdentity model);
    /// <summary>擦除当前活动模型及其递归依赖（ProMdlEraseAll，静态，动作型）。</summary>
    void ModelEraseCurrentWithDependencies();
    /// <summary>擦除所有未显示的模型（ProMdlEraseNotDisplayed，静态，动作型）。</summary>
    void ModelEraseNotDisplayed();
    /// <summary>获取模型当前窗口 id（ProMdlWindowGet）；无窗口返回 null。</summary>
    int? ModelWindowGet(ModelIdentity model);
    /// <summary>读取模型数据三元组（文件名、通用名、类型）；不可读返回 null。</summary>
    ModelDataRecord? ModelDataGet(ModelIdentity model);
    /// <summary>枚举模型中所有 gtol（ProMdlGtolVisit）；无 gtol 返回空列表。</summary>
    IReadOnlyList<ItemRef> ModelGtolList(ModelIdentity model);

    // ---- GTol 写路径 ----
    /// <summary>在模型上创建形位公差(GTol)。返回新建 GTol 的 ItemRef。</summary>
    ItemRef GtolCreate(ModelIdentity model, CreoGtolType type, string valueString,
        double x, double y, double z);
    /// <summary>读回形位公差类型和值字符串。</summary>
    GtolInfo GtolRead(ModelIdentity model, int gtolId);
    /// <summary>删除形位公差。</summary>
    void GtolDelete(ModelIdentity model, int gtolId);

    // ---- P7 Solid Visitor ----
    /// <summary>Ctk_SolidAxisList: 枚举实体轴；无则返空列表。</summary>
    IReadOnlyList<ItemRef> SolidAxisList(ModelIdentity model);
    /// <summary>Ctk_SolidCsysList: 枚举坐标系；无则返空列表。</summary>
    IReadOnlyList<ItemRef> SolidCsysList(ModelIdentity model);
    /// <summary>Ctk_SolidSurfaceList: 枚举曲面；无则返空列表。</summary>
    IReadOnlyList<ItemRef> SolidSurfaceList(ModelIdentity model);
    /// <summary>Ctk_SolidQuiltList: 枚举面组；无则返空列表。</summary>
    IReadOnlyList<ItemRef> SolidQuiltList(ModelIdentity model);
    /// <summary>Ctk_SolidDimensionList: 枚举尺寸；refDimMode=1 含参考尺寸。</summary>
    IReadOnlyList<ItemRef> SolidDimensionList(ModelIdentity model, bool includeRefDimensions);
    double? DimensionValueGet(ItemRef dimension);
    int? DimensionTypeGet(ItemRef dimension);
    string? DimensionTextGet(ItemRef dimension);

    // 3D/2D/GTol 尺寸附着信息读回(ProDimensionAttachmentsGet /
    // ProDrawingDimAttachpointsGet / ProGtolAttachMakeDimGet)。返回附着 pair 数 + senses 详情;
    // handle 生命周期归 native 释放(ProDimattachmentarrayFree),暂不外露。null=不可读/不存在。
    /// <summary>ProDimensionAttachmentsGet: 3D 尺寸的 annotation plane / senses / attach 数量。</summary>
    DimensionAttachmentsInfo? DimensionAttachmentsGet(ItemRef dimension);
    /// <summary>ProDrawingDimAttachpointsGet: 图纸尺寸的 attach 数量 / senses(annotation plane 图纸场景不适用)。</summary>
    DimensionAttachmentsInfo? DrawingDimensionAttachmentsGet(ModelIdentity drawing, ItemRef dimension);
    /// <summary>ProGtolAttachMakeDimGet: GTol 附着的 make-dim 参数(plane+attach+senses+orient+location)。</summary>
    DimensionAttachmentsInfo? GtolAttachMakeDimGet(ModelIdentity ownerModel, IntPtr gtolAttach);

    /// <summary>Bridge 组合流程:ProAnnotationplaneFlatToScreenCreate + ProGtolAttachAlloc +
    /// ProGtolAttachFreeSet + ProGtolAttachMakeDimGet + ProGtolAttachFree,业务侧无需处理
    /// IntPtr 生命周期。用于在真 GtolCreate 前预览 attach 的 make-dim 信息。</summary>
    DimensionAttachmentsInfo? PrepareGtolAttachAndReadMakeDim(ModelIdentity model, double x, double y, double z);

    // 剩余 4 heap-out 函数 Bridge 封装。前 2 是 owned mode:data,
    // 后 2 是 borrowed/unknown 语义特殊(不外露 raw ProArray 句柄防误 free)。
    /// <summary>ProSnapshotTrfsGet: 读取 snapshot 每个组件的变换矩阵(相对顶层)。</summary>
    IReadOnlyList<SnapshotTransform>? SnapshotTrfsGet(string snapshotName);
    /// <summary>ProSnapshotCreate: 以当前屏幕位置创建顶层装配 snapshot(roundtrip 验证用)。</summary>
    void SnapshotCreate(string snapshotName);
    /// <summary>ProSnapshotDelete: 按名删除顶层装配 snapshot;不存在时静默。</summary>
    void SnapshotDelete(string snapshotName);
    /// <summary>ProSeldatSelboxesGet: 读取 selection data 内所有 selection box 对角点。</summary>
    IReadOnlyList<SelectionBoxData>? SeldatSelboxesGet(IntPtr selData);
    /// <summary>ProEdgedataGet: 拆解 pro_edge_data 结构读回 edge id/两侧 surface/两侧方向 + uv 计数。
    /// uv 数组是 borrowed(归 ProEdgedataFree 嵌套 free 管),句柄不外露。</summary>
    EdgeDataSnapshot? EdgedataGet(CreoToolkit.Interop.Generated.pro_edge_data edgeData);
    /// <summary>ProCableLocationsOnSegEndGet: 读取 cable 每段末端 boundary(start/end 两 model item)。
    /// 语义 unknown(保守 borrowed):头文件无 free 句,raw 不外露,PTC 澄清或真机验证后再转 owned。</summary>
    IReadOnlyList<CableSegmentBoundary>? CableLocationsOnSegEndGet(ItemRef cable);

    // 图纸尺寸写路径(2026-07-06):ProDrawingDimensionCreate / ProDimensionDelete。
    // create 出的尺寸天然携带 attachments,与 DrawingDimensionAttachmentsGet 组成
    // create→读回→delete roundtrip(读回 API 有值路径的真机验证钥匙)。
    /// <summary>ProDrawingDimensionCreate: 程序化创建图纸尺寸(attach+sense 等长,全 attach
    /// 关联 viewId)。drawing/view 不存在返 null;native 创建失败抛异常(写操作硬失败)。</summary>
    ItemRef? DrawingDimensionCreate(
        ModelIdentity drawing,
        IReadOnlyList<ItemRef> attachItems,
        IReadOnlyList<DimSenseInfo> senses,
        int orientHint,
        (double X, double Y, double Z) location,
        bool refDim,
        int viewId);
    /// <summary>ProDimensionDelete: 删除尺寸(solid/drawing 通用);失败抛异常。</summary>
    void DimensionDelete(ItemRef dimension);

    // 图纸坐标式尺寸(2026-07-06 续):UgDrawingDimensions.c 后半段。
    // 三 API 共同副作用:入参线性 dim 会被**就地转成 ordinate**(非 Create 语义)。
    /// <summary>ProDrawingOrdbaselineCreate: 就地把线性 dim 转 ordinate baseline;
    /// <paramref name="location"/> = 3D solid 坐标下贴近欲作 baseline 端 attach 实体的点
    /// (选端点用,tkuse 语义)。drawing 不在会话返 null;native 失败抛异常。</summary>
    ItemRef? DrawingOrdbaselineCreate(
        ModelIdentity drawing, ItemRef linearDim, (double X, double Y, double Z) location);
    /// <summary>ProDrawingDimToOrdinate: 用已建 baseline 就地把线性 dim 转 ordinate;
    /// drawing/owner 不在会话或 native 失败抛异常。</summary>
    void DrawingDimToOrdinate(ModelIdentity drawing, ItemRef linearDim, ItemRef baseline);
    /// <summary>ProDrawingDimIsOrdinate: 读回是否 ordinate + 关联 baseline。
    /// drawing 不在会话 / native 非 NO_ERROR 归 null;IsOrdinate=false 时 Baseline=null。</summary>
    (bool IsOrdinate, ItemRef? Baseline)? DrawingDimIsOrdinate(ModelIdentity drawing, ItemRef dim);
    /// <summary>ProDrawingDimToLinear: 就地把 ordinate dim 转回线性(tkuse 59537)。
    /// drawing/owner 不在会话或 native 失败抛异常;写路径硬失败契约。</summary>
    void DrawingDimToLinear(ModelIdentity drawing, ItemRef ordinateDim);
    /// <summary>Ctk_SolidRelsetList: 枚举关系集；无则返空列表。</summary>
    IReadOnlyList<ItemRef> SolidRelsetList(ModelIdentity model);

    // ---- TestMeasure 几何评估 (ProGeomitem*Eval, 直绑头文件) ----
    /// <summary>两几何项夹角(度);跨模型/NotFound 返 null。Angle 要求 2 个 edge/线性几何项。</summary>
    double? GeomitemAngleEval(ItemRef item1, ItemRef item2);
    /// <summary>两几何项距离(模型长度单位);跨模型/NotFound 返 null。Distance 接受 surface/point/axis。</summary>
    double? GeomitemDistanceEval(ItemRef item1, ItemRef item2);
    /// <summary>单 surface 直径(仅圆柱/圆锥);NotFound 返 null。</summary>
    double? GeomitemDiameterEval(ItemRef item);

    // ---- P7 Drawing Visitor ----
    /// <summary>Ctk_DrawingTableList: 枚举图纸表格；无则返空列表。</summary>
    IReadOnlyList<ItemRef> DrawingTableList(ModelIdentity model);
    /// <summary>Ctk_DrawingDimensionList: 枚举图纸尺寸；dimType=0 全部。</summary>
    IReadOnlyList<ItemRef> DrawingDimensionList(ModelIdentity model, int dimType);
    /// <summary>Ctk_DrawingDtlnoteList: 枚举详细注释；sheet=0 全部页。</summary>
    IReadOnlyList<ItemRef> DrawingDtlnoteList(ModelIdentity model, int sheet);
    /// <summary>ProDrawingDtlgroupsCollect: 枚举 draft group(dtlgroup) 容器项;sheet=0 全部, sheet&gt;0 限定该页。
    /// 出口 <see cref="ItemRef"/>(type=DraftGroup);Sketch tab 交互式画的 draft 几何常经 dtlgroup 承载。</summary>
    IReadOnlyList<ItemRef> DrawingDtlgroupsList(ModelIdentity model, int sheet);

    // ---- pt_drawing draft entity 接缝 ----
    /// <summary>ProDrawingDtlentitiesCollect: 枚举图纸/页内 draft entities;sheet=0 全部, sheet&gt;0 限定该页。
    /// 出口 <see cref="CreoDtlentity"/> 携 <paramref name="sessionEpoch"/> 用于跨 session 误传守门。
    /// OOM 时直接抛 <see cref="CreoException"/>(ErrorCode=OutOfMemory),不映射 NotFound。</summary>
    IReadOnlyList<CreoDtlentity> DrawingDtlentitiesList(ModelIdentity drawing, int sheet, long sessionEpoch);

    /// <summary>ProDtlentityDataGet: 读 draft entity 详细数据。
    /// owner 已 erase / 同名跨 Load id 失效 / 3 sub-Get(Color/Font/Width) 之一失败 → 返 null + bridge trace 含 reason 后缀。</summary>
    CreoDtlentityData? DrawingDtlentityDataGet(ModelIdentity drawing, CreoDtlentity entity);

    /// <summary>ProDtlentityCreate: 在 drawing 上创建 draft entity(Create 方向,fixture 程序化生成)。
    /// 返回创建的 entity id; 失败返 null + bridge trace 含 reason。</summary>
    int? DrawingDtlentityCreate(ModelIdentity drawing, CreoDtlentityCurve curve, CreoColor color);

    // ---- 工程图表格写路径 ----
    /// <summary>ProDrawingTableCreate 序列: 在 drawing 上创建表格并写入单元格文本;返回 table ItemRef。</summary>
    ItemRef DrawingTableCreate(ModelIdentity drawing, DrawingTableSpec spec);

    /// <summary>ProDwgtableCelltextGet(mode=1): 读表格单元格文本;空格返空数组。</summary>
    string[] DrawingTableCellRead(ModelIdentity drawing, int tableId, int column, int row);

    /// <summary>ProDwgtableDelete: 删除表格。</summary>
    void DrawingTableDelete(ModelIdentity drawing, int tableId);

    /// <summary>ProDwgtableRowsCount: 表格行数。</summary>
    int DrawingTableRowsCount(ModelIdentity drawing, int tableId);

    /// <summary>ProDwgtableColumnsCount: 表格列数。</summary>
    int DrawingTableColumnsCount(ModelIdentity drawing, int tableId);

    // ---- P7 Load/Init ----
    /// <summary>Ctk_ModelLoad: 从磁盘打开模型；失败或路径无效返 null。</summary>
    ModelIdentity? ModelLoad(string path, CreoModelType fileType, bool askUserAboutReps);
    /// <summary>Ctk_ModelInit: 在会话中新建空模型（动作型）。</summary>
    void ModelInit(string name, CreoModelType fileType);

    // ---- 事件订阅: TestNotify (PoC 单事件,后续按需扩) ----
    /// <summary>订阅 PRO_DIRECTORY_CHANGE_POST 事件 (ProNotificationSet 直绑):
    /// 当 Creo 工作目录变更时调 handler(newPath)。Dispose 返回值 → ProNotificationUnset 清订阅 +
    /// 释放 GCHandle root。**同 type 仅允许一个订阅**, 重复订阅抛 InvalidOperationException
    /// (C 原生覆盖语义在 SDK 层加守卫,避免 callback 静默失效)。</summary>
    INativeResource NotifySubscribeDirectoryChanged(Action<string> handler);
    /// <summary>订阅 PRO_MDL_SAVE_PRE 事件 (ProNotificationSet 直绑):
    /// 当 Creo 保存模型前调 handler(待保存模型的完整路径)。基于新事件 PRO_MODEL_SAVE_PRE
    /// (取代 deprecated PRO_MDL_SAVE_PRE);Creo 4 callback 入参 = ProPath wstring(非 ProMdl handle),
    /// bridge 用 PtrToStringUni 解码为托管 string(DATA)。Dispose 返回值 → ProNotificationUnset 清订阅 +
    /// 释放 GCHandle root。**同 type 仅允许一个订阅**。</summary>
    INativeResource NotifySubscribeModelSavePre(Action<string> handler);

    /// <summary>订阅 PRO_GROUP_UNGROUP_PRE 拦截事件(GroupUngroupPre):
    /// Creo UI 触发 ungroup 前回调,handler 返 <see cref="UngroupVote.Allow"/> 放行 /
    /// <see cref="UngroupVote.Block"/> 拦截。Dispose 返回值 → ProNotificationUnset + 释放 GCHandle root。
    /// **同 type 仅允许一个订阅**;重订阅抛 InvalidOperationException。
    /// 拦截型 SEH 特例:handler 抛异常默认 Allow(避免误拦阻断 UI)+ warn 日志。</summary>
    INativeResource NotifySubscribeGroupUngroupPre(Func<CreoFeatureGroupContext, UngroupVote> handler);

    // ---- Axis (DATA) ----
    /// <summary>取轴线段端点(ProAxisDataGet → ProLinedata: end1[3]+end2[3], DATA copy-out);
    /// 方向 = End2 - End1（未归一化）。不可读返 null。</summary>
    AxisLineData? AxisLineDataGet(ItemRef axis);
    /// <summary>取拥有该轴的 surface(ProAxisSurfaceGet); 无关联面返 null。</summary>
    ItemRef? AxisOwnerSurfaceGet(ItemRef axis);

    // ---- Edge (DATA) ----
    /// <summary>ProEdgeTypeGet; 值对应 pro_ent_type: LINE=1/ARC=2 等。不可读返 null。</summary>
    int? EdgeTypeGet(ItemRef edge);
    /// <summary>ProEdgeLengthEval; 不可读返 null。</summary>
    double? EdgeLengthGet(ItemRef edge);
    /// <summary>ProEdgeNeighborsGet → 两相邻面 ItemRef; 单侧边 Face2 为 null。不可读两者均 null。</summary>
    (ItemRef? Face1, ItemRef? Face2) EdgeNeighborSurfacesGet(ItemRef edge);

    // ---- Curve (DATA) ----
    /// <summary>ProCurveLengthEval; 不可读返 null。</summary>
    double? CurveLengthGet(ItemRef curve);
    /// <summary>ProCurveTypeGet; 值对应 pro_ent_type。不可读返 null。</summary>
    int? CurveTypeGet(ItemRef curve);

    // ---- Quilt (DATA) ----
    /// <summary>ProQuiltSurfaceVisit → 收集 quilt 内所有面; 无面返空列表。</summary>
    IReadOnlyList<ItemRef> QuiltSurfaceList(ItemRef quilt);
    /// <summary>ProQuiltVolumeEval; 不可读返 null。</summary>
    double? QuiltVolumeEval(ItemRef quilt);
    /// <summary>ProQuiltIsBackupgeometry; 不可读返 null。</summary>
    bool? QuiltIsBackupGeometry(ItemRef quilt);

    // ---- Layer Item (DATA) ----
    /// <summary>ProLayerItemsPopulate → 列出层包含的所有项; 空层返空列表。</summary>
    IReadOnlyList<ItemRef> LayerItemsList(ItemRef layer);
}

/// <summary>
/// 一份 owned native 资源(LIVE 句柄或 BORROWED 副本集)的抽象。L3 拥有它, 在作用域结束时
/// <see cref="IDisposable.Dispose"/> 经 dispatcher 在主线程释放。真实实现内部持 CreoSafeHandle +
/// function-specific free(如 elemtree 的 ProFeatureElemtreeFree 带 feature 上下文); 测试实现计数。
/// </summary>
internal interface INativeResource : IDisposable
{
    /// <summary>是否已释放(用于防重复释放与诊断)。</summary>
    bool IsDisposed { get; }
}

internal interface IWrappedNativeResource : INativeResource
{
    INativeResource Inner { get; }
}

/// <summary>模型标识(内部 token): 名 + 类型。名已 copy-out 为 DATA, 不持 ProMdl 指针跨调用。</summary>
internal readonly record struct ModelIdentity(string Name, CreoModelType Type);

/// <summary>模型数据三元组（Ctk_ModelDataGet DATA 快照）。</summary>
public readonly record struct ModelDataRecord(string FileName, string GenericName, CreoModelType ModelType);

/// <summary>模型项引用(内部 token, ItemRef): 模型标识 + 项类型 + 项 id。
/// 携带 ModelIdentity 以保住 elemtree 释放等 owning 上下文。不进入 public 面;
/// ModelItem v1 衍生只 Feature 实施()), 其余 type 留 future。</summary>
internal readonly record struct ItemRef(ModelIdentity Model, CreoModelItemType Type, int Id);

/// <summary>PLANE 类型面几何数据快照(对齐 Pro/Toolkit <c>Ptc_plane { e1[3], e2[3], e3[3], origin[3] }</c>)。
/// L2↔L3 接缝保持 <c>double[]</c> POD(不直接装 CreoVec3,避免 L2 ABI 污染 L3 类型)。
/// L3 包装侧 <c>CreoPlane.FromPtcPlane(Origin, E3)</c>(e1/e2 当前仅留作 future 面内基扩展)。</summary>
internal readonly record struct SurfacePlaneData(double[] E1, double[] E2, double[] E3, double[] Origin);

/// <summary>轴线段端点快照(ProAxisDataGet → ProLinedata)。end1/end2 各 3-double，方向 = end2 - end1。</summary>
internal readonly record struct AxisLineData(double[] End1, double[] End2);

/// <summary>元素树抽取结果: owned 树句柄 + 已 copy-out 的节点摘要(只读遍历用)。</summary>
internal sealed record ElemtreeExtractResult(INativeResource Tree, IReadOnlyList<CreoElementNode> Nodes);

/// <summary>深度元素树抽取结果: owned 树句柄 + 带类型化值的节点摘要。</summary>
internal sealed record ElemtreeExtractDeepResult(INativeResource Tree, IReadOnlyList<CreoRichElementNode> Nodes);

/// <summary>选择项摘要: 副本集中的稳定索引 + 显示标签(DATA) + 强类型 ModelItem 引用())。
/// ItemType/ItemId 直接来自 native <c>ProModelitem</c>(ProType + id), 上层 L3 据此包成
/// <see cref="ICreoModelItem"/>(v1 只 Feature, 其它返回 null)。
/// <para>Owner=per-item 归属模型(交互选择可跨模型;<c>pro_model_item.owner</c> 反查,不可解析为 null,
/// null 时物化回退集合级 owner);RawTypeCode=原始 <c>ProType</c> int(Enum 未定义值不丢,0=未填);
/// Path=装配组件路径接缝 DATA(ProSelectionAsmcomppathGet;part/顶层场景或抽取失败为 null)。</para></summary>
internal readonly record struct SelectionItemData(
    int Index, string Label, CreoModelItemType ItemType, int ItemId,
    ModelIdentity? Owner = null, int RawTypeCode = 0, ComponentPathData? Path = null);

/// <summary>装配组件路径接缝 DATA(native <c>pro_comp_path</c>{owner, comp_id_table[25], table_num}
/// 的降维):Root=顶层装配,ComponentIds=从 Root 逐级向下的 component feature id 链(1..25)。
/// L3 面物化为 <see cref="CreoComponentPath"/>(对齐 OTK pfcComponentPath)。</summary>
internal readonly record struct ComponentPathData(ModelIdentity Root, IReadOnlyList<int> ComponentIds)
{
    /// <summary>native comp_id_table 定长 25（与 ProAsmcomppathInit SizeConst 一致）。</summary>
    internal const int MaxDepth = 25;
}

/// <summary>程序选择结果: owned 副本集句柄 + 各项摘要。</summary>
internal sealed record SelectResult(INativeResource Set, IReadOnlyList<SelectionItemData> Items);
