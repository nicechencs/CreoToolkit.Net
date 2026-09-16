using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Errors;

/// <summary>
/// 按<em>函数级</em>语义将 <see cref="ProError"/> 分类为 <see cref="CreoOutcome"/>。
/// 陷阱：同一数值码在不同 API 含义不同，必须按 (api, code) 对而非单凭数值判断。
/// <para>
/// 分类由<b>声明式表</b>（<see cref="Table"/>）驱动，是唯一真源：只有显式登记的 api 才享有
/// 良性语义；未登记的一律走保守默认（仅 <see cref="ProError.NoError"/> 为成功，其余皆错误）。
/// 不再有任何基于命名（<c>EndsWith("Get")</c> 等）的启发式——拼错或新增 api 会安全退回最严格的
/// 保守默认并暴露，而非被静默吞成良性。表键用 <c>nameof(G.ProXxx)</c> 编译期绑定到生成的
/// <c>NativeMethods</c>，拼错即编译失败；测试再反射兜底。
/// </para>
/// 原始码通过 <see cref="CreoResult"/> 无损保留。
/// </summary>
public static class ProErrorPolicy
{
    /// <summary>
    /// 已登记 api 的错误语义类别。每类定义一组 (code → outcome) 映射，见 <see cref="ClassifyByPolicy"/>。
    /// </summary>
    private enum ErrorPolicy
    {
        /// <summary>
        /// 查询/读取类 getter：目标属性/对象不存在时返回 <see cref="ProError.NotFound"/>，语义为
        /// “无此值”（<see cref="CreoOutcome.ConditionNegative"/>），非错误；<see cref="ProError.Found"/>
        /// 为命中（<see cref="CreoOutcome.ConditionPositive"/>）。其余非零码仍走保守默认。
        /// </summary>
        QueryLookup,
    }

    /// <summary>
    /// 声明式分类表（单一真源）：api 名 → 策略。所有条目均为 <see cref="ErrorPolicy.QueryLookup"/>
    /// ——纯读取型 getter，其 NOT_FOUND 表示“所查属性/对象不存在”，是良性查询条件而非失败。
    /// 每条对应 Creo Toolkit 头文件里一个明确的读取语义函数。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, ErrorPolicy> Table =
        new Dictionary<string, ErrorPolicy>(StringComparer.Ordinal)
        {
            // ---- Model / ProMdl：模型属性读取，模型不在会话或属性未设时 NOT_FOUND=无此值 ----
            [nameof(G.ProMdlCurrentGet)] = ErrorPolicy.QueryLookup,          // 当前无活动模型
            [nameof(G.ProMdlnameRetrieve)] = ErrorPolicy.QueryLookup,        // 按名 search_path 检索模型
            [nameof(G.ProMdlMdlnameGet)] = ErrorPolicy.QueryLookup,          // 模型名
            [nameof(G.ProMdlTypeGet)] = ErrorPolicy.QueryLookup,             // 模型类型
            [nameof(G.ProMdlCommonnameGet)] = ErrorPolicy.QueryLookup,       // 通用名（未设则无）
            [nameof(G.ProMdlOriginGet)] = ErrorPolicy.QueryLookup,           // 原始路径
            [nameof(G.ProMdlDataGet)] = ErrorPolicy.QueryLookup,             // 模型数据块
            [nameof(G.ProMdlLayerGet)] = ErrorPolicy.QueryLookup,            // 按名取层（无同名层）
            [nameof(G.ProMdlLockGet)] = ErrorPolicy.QueryLookup,             // 锁状态（未锁则无）
            [nameof(G.ProMdlVerstampGet)] = ErrorPolicy.QueryLookup,         // 版本戳
            [nameof(G.ProMdlWindowGet)] = ErrorPolicy.QueryLookup,           // 关联窗口（无则未显示）
            [nameof(G.ProMdlDirectoryPathGet)] = ErrorPolicy.QueryLookup,    // 目录路径
            [nameof(G.ProMdlDisplaynameGet)] = ErrorPolicy.QueryLookup,      // 显示名
            [nameof(G.ProMdlFiletypeGet)] = ErrorPolicy.QueryLookup,         // 文件类型
            [nameof(G.ProMdlIdGet)] = ErrorPolicy.QueryLookup,               // 模型 id
            [nameof(G.ProMdlObjectdefaultnameGet)] = ErrorPolicy.QueryLookup,// 对象默认名
            [nameof(G.ProMdlSubtypeGet)] = ErrorPolicy.QueryLookup,          // 模型子类型

            // ---- Modelitem：条目属性读取，条目无名/无所属时 NOT_FOUND=无此值 ----
            [nameof(G.ProModelitemMdlGet)] = ErrorPolicy.QueryLookup,        // 条目所属模型
            [nameof(G.ProModelitemNameGet)] = ErrorPolicy.QueryLookup,       // 条目名
            [nameof(G.ProModelitemDefaultnameGet)] = ErrorPolicy.QueryLookup,// 条目默认名

            // ---- Parameter / Unit：参数与单位读取，参数无该属性时 NOT_FOUND=未设置 ----
            [nameof(G.ProParameterValueGet)] = ErrorPolicy.QueryLookup,            // 参数值
            [nameof(G.ProParameterValueWithUnitsGet)] = ErrorPolicy.QueryLookup,   // 带单位参数值
            [nameof(G.ProParameterCreate)] = ErrorPolicy.QueryLookup,              // 历史直绑键(WithUnits 后继并存)
            [nameof(G.ProParameterWithUnitsCreate)] = ErrorPolicy.QueryLookup,     // 参数创建(WithUnits 后继)
            [nameof(G.ProParameterValueSet)] = ErrorPolicy.QueryLookup,            // 历史直绑键(WithUnits 后继并存)
            [nameof(G.ProParameterValueWithUnitsSet)] = ErrorPolicy.QueryLookup,   // 参数更新(WithUnits 后继)
            [nameof(G.ProParameterUnitsGet)] = ErrorPolicy.QueryLookup,            // 参数单位（无量纲则无）
            [nameof(G.ProParameterDescriptionGet)] = ErrorPolicy.QueryLookup,      // 参数描述（未填则无）
            [nameof(G.ProUnitExpressionGet)] = ErrorPolicy.QueryLookup,            // 单位表达式
            [nameof(G.ProUnitTypeGet)] = ErrorPolicy.QueryLookup,                  // 单位类型
            [nameof(G.ProUnitsystemTypeGet)] = ErrorPolicy.QueryLookup,            // 单位制类型

            // ---- Solid / Part / Family：实体与零件属性读取 ----
            [nameof(G.ProSolidMassPropertyGet)] = ErrorPolicy.QueryLookup,         // 质量属性
            [nameof(G.ProSolidOutlineGet)] = ErrorPolicy.QueryLookup,              // 外框
            [nameof(G.ProSolidAccuracyGet)] = ErrorPolicy.QueryLookup,             // 精度设置
            [nameof(G.ProSolidRegenerationstatusGet)] = ErrorPolicy.QueryLookup,   // 再生状态
            [nameof(G.ProPartMaterialsGet)] = ErrorPolicy.QueryLookup,             // 材料列表（无则空）
            [nameof(G.ProPartDensityGet)] = ErrorPolicy.QueryLookup,               // 密度（未设则无）
            [nameof(G.ProFaminstanceGenericGet)] = ErrorPolicy.QueryLookup,        // 族实例的 generic（非实例则无）

            // ---- 几何 / Csys / Point / Surface / Config：几何与配置读取 ----
            [nameof(G.ProCsysDataGet)] = ErrorPolicy.QueryLookup,           // 坐标系数据
            [nameof(G.ProPointCoordGet)] = ErrorPolicy.QueryLookup,         // 点坐标
            [nameof(G.ProSurfaceTypeGet)] = ErrorPolicy.QueryLookup,        // 曲面类型
            [nameof(G.ProConfigoptionGet)] = ErrorPolicy.QueryLookup,       // config 选项（未设则无）
            [nameof(G.ProConfigoptArrayGet)] = ErrorPolicy.QueryLookup,     // config 多值项（未设则空列表，NOT_FOUND 良性）

            // ---- Assembly path：装配路径解析，路径无对应件时 NOT_FOUND=无此件 ----
            [nameof(G.ProAsmcomppathMdlGet)] = ErrorPolicy.QueryLookup,     // 路径末端模型
            [nameof(G.ProAsmcomppathTrfGet)] = ErrorPolicy.QueryLookup,     // 路径变换矩阵

            // ---- Drawing / Dimension / Dtl / Note：图纸与标注读取 ----
            [nameof(G.ProDrawingCurrentSheetGet)] = ErrorPolicy.QueryLookup,// 当前图页
            [nameof(G.ProDimensionTextGet)] = ErrorPolicy.QueryLookup,      // 尺寸文本
            [nameof(G.ProDimensionTextWstringsGet)] = ErrorPolicy.QueryLookup, // 尺寸文本(多行 wstring)
            [nameof(G.ProDimensionTypeGet)] = ErrorPolicy.QueryLookup,      // 尺寸类型
            [nameof(G.ProDimensionValueGet)] = ErrorPolicy.QueryLookup,     // 尺寸值
            [nameof(G.ProDtlentityDataGet)] = ErrorPolicy.QueryLookup,      // 详图实体数据
            [nameof(G.ProCurvedataTypeGet)] = ErrorPolicy.QueryLookup,     // curve 类型(无 curve 则 NOT_FOUND)
            [nameof(G.ProNoteURLWstringGet)] = ErrorPolicy.QueryLookup,     // 注释 URL（无则无）

            // ---- 其余读取型 ----
            [nameof(G.ProLayerDisplaystatusGet)] = ErrorPolicy.QueryLookup, // 层显示状态
            [nameof(G.ProVerstampStringGet)] = ErrorPolicy.QueryLookup,     // 版本戳字符串
            [nameof(G.ProArraySizeGet)] = ErrorPolicy.QueryLookup,          // ProArray 尺寸（空/未分配则无）
            [nameof(G.ProWindowCurrentGet)] = ErrorPolicy.QueryLookup,      // 当前窗口
        };

    /// <summary>已登记的 api 名集合，供测试反射校验其均命中 <c>NativeMethods</c>。</summary>
    public static IReadOnlyCollection<string> RegisteredApiNames => Table.Keys;

    /// <summary>
    /// 在 <paramref name="api"/> 的语义上对状态码 <paramref name="e"/> 分类。
    /// 优先查声明表；无匹配（未登记 api 或该策略未覆盖此码）则退回保守默认。
    /// </summary>
    /// <param name="api">Creo TOOLKIT 函数名，建议以 <c>nameof(G.ProXxx)</c> 传入。</param>
    /// <param name="e">该函数返回的原始 native 状态码。</param>
    public static CreoOutcome Classify(string api, ProError e)
    {
        // NO_ERROR 对所有 api 无条件为成功。
        if (e == ProError.NoError)
            return CreoOutcome.Success;

        if (Table.TryGetValue(api, out var policy)
            && ClassifyByPolicy(policy, e) is { } outcome)
            return outcome;

        return Conservative(e);
    }

    /// <summary>按策略类别把非零码映射为良性 <see cref="CreoOutcome"/>；不属该策略覆盖范围返回 null。</summary>
    private static CreoOutcome? ClassifyByPolicy(ErrorPolicy policy, ProError e) => policy switch
    {
        ErrorPolicy.QueryLookup => e switch
        {
            ProError.NotFound => CreoOutcome.ConditionNegative,
            ProError.Found => CreoOutcome.ConditionPositive,
            _ => null, // 查询类的其他非零码仍走保守默认
        },
        _ => null,
    };

    /// <summary>
    /// 无显式声明时的保守默认：仅 <see cref="ProError.NoError"/> 为成功，其余均为错误。
    /// 未分类的非零码会主动暴露，而不是被静默吞掉。
    /// </summary>
    private static CreoOutcome Conservative(ProError e)
        => e == ProError.NoError ? CreoOutcome.Success : CreoOutcome.Error;
}
