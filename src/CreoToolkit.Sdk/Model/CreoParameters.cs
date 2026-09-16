using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Native;
using ProUnitType = CreoToolkit.Interop.Generated.ProUnitType;

namespace CreoToolkit.Sdk;

/// <summary>
/// 参数门面。**默认即时快照**(DATA: copy-out-free-in), 不暴露 live enumerator——后者会把
/// dispatcher/模型态/native lifetime 拖进用户 foreach。大模型上万参数走**分级 API**:
/// <see cref="Find"/> 直查 / <see cref="List()"/> 全量 / <see cref="List(ParameterQuery)"/> 过滤 /
/// <see cref="ListPage"/> 分页。集合一律 <see cref="IReadOnlyList{T}"/>(泛型, 不装箱)。session-bound。
/// </summary>
public sealed class CreoParameters
{
    private readonly CreoSession _session;
    private readonly ModelIdentity _model;

    internal CreoParameters(CreoSession session, ModelIdentity model)
    {
        _session = session;
        _model = model;
    }

    /// <summary>直查单个参数(单项快照); NOT_FOUND → null。</summary>
    public CreoParameter? Find(string name)
    {
        ValidateParameterName(name);
        var result = _session.Run(n => n.ParameterFind(_model, name));
        CreoSdkLog.Trace("parameter", "find",
            new { model = _model.Name, name, found = result != null });
        return result;
    }

    /// <summary>一次性全量快照(适合普通模型)。</summary>
    public IReadOnlyList<CreoParameter> List()
    {
        var result = _session.Run(n => n.ParameterList(_model, null));
        CreoSdkLog.Trace("parameter", "list",
            new { model = _model.Name, count = result.Count });
        return result;
    }

    /// <summary>
    /// 过滤后的快照。过滤**下推到接缝**(L1 支持则下推, 否则接缝侧取全量再过滤)——避免大模型把上万
    /// 参数全拉回 L3 再筛。空条件等价 <see cref="List()"/>。
    /// </summary>
    public IReadOnlyList<CreoParameter> List(ParameterQuery query)
    {
        if (query.IsEmpty) return List();
        var result = _session.Run(n => n.ParameterList(_model, query));
        CreoSdkLog.Trace("parameter", "list",
            new { model = _model.Name, count = result.Count, prefix = query.NamePrefix, kind = query.Kind?.ToString() });
        return result;
    }

    /// <summary>分页快照(大集合用)。offset/limit 须非负。</summary>
    public IReadOnlyList<CreoParameter> ListPage(int offset, int limit)
    {
        ThrowUtil.IfNegative(offset);
        ThrowUtil.IfNegative(limit);
        var result = _session.Run(n => n.ParameterListPage(_model, offset, limit));
        CreoSdkLog.Trace("parameter", "listpage",
            new { model = _model.Name, offset, limit, count = result.Count });
        return result;
    }

    /// <summary>设置参数值(动作型, 经 dispatcher; **upsert**: 参数存在则更新, 不存在则按值类型新建)。</summary>
    public void Set(string name, CreoParamValue value) =>
        CreoSdkLog.Run(
            "parameter", "set",
            body: () =>
            {
                ValidateParameterName(name);
                _session.Run(n => n.ParameterSet(_model, name, value));
            },
            failProps: () => new { model = _model.Name, name },
            okProps: () => new { model = _model.Name, name, kind = value.Kind.ToString() });

    /// <summary>删除参数(动作型)。参数不存在返回 false, 不抛。</summary>
    public bool Delete(string name) =>
        CreoSdkLog.Run(
            "parameter", "delete",
            body: () =>
            {
                ValidateParameterName(name);
                return _session.Run(n => n.ParameterDelete(_model, name));
            },
            failProps: () => new { model = _model.Name, name },
            okProps: deleted => new { model = _model.Name, name, deleted });

    /// <summary>重置参数到上次设置前的值。参数不存在时返回 false。</summary>
    public bool Reset(string name) =>
        CreoSdkLog.Run(
            "parameter", "reset",
            body: () =>
            {
                ValidateParameterName(name);
                return _session.Run(n => n.ParameterReset(_model, name));
            },
            failProps: () => new { model = _model.Name, name },
            okProps: reset => new { model = _model.Name, name, reset });

    /// <summary>读取参数 designation 状态。参数不存在时返回 null(区别于"存在但未 designated"=false)。</summary>
    public bool? IsDesignated(string name)
    {
        ValidateParameterName(name);
        var result = _session.Run(n => n.ParameterIsDesignated(_model, name));
        CreoSdkLog.Trace("parameter", "isdesignated",
            new { model = _model.Name, name, hasValue = result.HasValue, value = result });
        return result;
    }

    /// <summary>设置参数 designation 状态。参数不存在时返回 false。</summary>
    public bool SetDesignated(string name, bool designated) =>
        CreoSdkLog.Run(
            "parameter", "setdesignated",
            body: () =>
            {
                ValidateParameterName(name);
                return _session.Run(n => n.ParameterSetDesignated(_model, name, designated));
            },
            failProps: () => new { model = _model.Name, name },
            okProps: ok => new { model = _model.Name, name, designated, applied = ok });

    /// <summary>读取参数 description(DATA, ProParameterDescriptionGet 直绑)。
    /// 参数不存在或无 description 返回 null。</summary>
    public string? GetDescription(string name)
    {
        ValidateParameterName(name);
        var result = _session.Run(n => n.ParameterDescription(_model, name));
        CreoSdkLog.Trace("parameter", "getdescription",
            new { model = _model.Name, name, hasValue = result != null });
        return result;
    }

    // ---- 单位 (DATA copy-out) ----

    /// <summary>读带单位参数(返参数自身单位下的值)。参数不存在 → null;参数 unitless → Unit==null。
    /// 要换单位读,用 <see cref="GetUnits"/> + <see cref="CreoModel.UnitInit"/> + ConvertTo 手动算
    /// (native ProParameterScaledvalueGet 无 target unit 入参,L3 不提供假能力)。</summary>
    public CreoParameterValueWithUnits? GetWithUnits(string name)
    {
        ValidateParameterName(name);
        var result = _session.Run(n => n.ParameterGetWithUnits(_model, name));
        CreoSdkLog.Trace("parameter", "getwithunits",
            new { model = _model.Name, name, found = result.HasValue, hasUnit = result?.Unit is not null });
        return result;
    }

    /// <summary>写带单位参数。
    /// unitless 参数 + non-null unit 抛 <see cref="UnitTypeMismatchException"/>:
    /// 需先 <see cref="Delete"/> 旧参数再以带单位形式重建(ProParameterUnitsAssign 对 unitless 不可升级)。</summary>
    public void SetWithUnits(string name, double value, CreoUnit? unit = null) =>
        CreoSdkLog.Run(
            "parameter", "setwithunits",
            body: () =>
            {
                ValidateParameterName(name);
                if (unit is { } u0)
                {
                    CreoUnitGuard.NotUninitialized(u0, nameof(unit));
                    EnsureUnitBelongsToModel(u0);
                }

                // 参数现状:决定走 Set(OfDouble) 还是 ParameterSetWithUnits + L3 quantity 守卫。
                var existing = _session.Run(n => n.ParameterFind(_model, name));
                var existingUnit = existing is null ? null : _session.Run(n => n.ParameterUnitsGet(_model, name));

                // 不存在 → upsert。unit=null 走 Set unitless,unit!=null 走带单位 Create。
                if (existing is null)
                {
                    if (unit is null)
                        _session.Run(n => { n.ParameterSet(_model, name, CreoParamValue.OfDouble(value)); return 0; });
                    else
                        _session.Run(n => { n.ParameterSetWithUnits(_model, name, value, unit); return 0; });
                    return;
                }

                // 既存 unitless:unit=null 等价 Set(double);unit!=null 抛(不能就地升级,要 Delete 重建)。
                if (existingUnit is null)
                {
                    if (unit is { } u1)
                        throw new UnitTypeMismatchException(
                            name, ProUnitType.PRO_UNITTYPE_LENGTH, u1.Type,
                            $"参数 '{name}' 是 unitless,不能赋单位 '{u1.Name}'。请先 Delete 旧参数再以带单位形式重建(AssignUnit 对 unitless 返 PRO_TK_E_NOT_FOUND 不可升级)。");
                    _session.Run(n => { n.ParameterSet(_model, name, CreoParamValue.OfDouble(value)); return 0; });
                    return;
                }

                // 既存带单位:unit!=null 时 quantity 必须匹配。
                if (unit is { } u2 && u2.Type != existingUnit.Value.Type)
                    throw new UnitTypeMismatchException(name, existingUnit.Value.Type, u2.Type);

                _session.Run(n => { n.ParameterSetWithUnits(_model, name, value, unit); return 0; });
            },
            failProps: () => new { model = _model.Name, name, value, unit_name = unit?.Name },
            okProps: () => new { model = _model.Name, name, value, unit_name = unit?.Name, unit_type = unit?.Type.ToString() });

    /// <summary>读参数自身单位(不做转换)。参数不存在 → null;unitless → null。
    /// 调用方区分"不存在"与"unitless"需配合 <see cref="Find"/> 检查。</summary>
    public CreoUnit? GetUnits(string name)
    {
        ValidateParameterName(name);
        var result = _session.Run(n => n.ParameterUnitsGet(_model, name));
        CreoSdkLog.Trace("parameter", "getunits",
            new { model = _model.Name, name, hasUnit = result.HasValue });
        return result;
    }

    /// <summary>改参数自身单位(ProParameterUnitsAssign);参数 unitless 时返 false。
    /// **副作用警告**:value 不变,数值含义被重新解释(7.85 g/cm³ → 7.85 kg/m³ 完全不同的物理量)— 谨慎用。
    /// unit 必须与参数原单位同 quantity,否则抛 <see cref="UnitTypeMismatchException"/>。</summary>
    public bool AssignUnit(string name, CreoUnit unit) =>
        CreoSdkLog.Run(
            "parameter", "assignunit",
            body: () =>
            {
                ValidateParameterName(name);
                CreoUnitGuard.NotUninitialized(unit, nameof(unit));
                EnsureUnitBelongsToModel(unit);
                // quantity 守卫:既存带单位时,unit 必须同 quantity 才能 assign。
                var existingUnit = _session.Run(n => n.ParameterUnitsGet(_model, name));
                if (existingUnit is { } eu && eu.Type != unit.Type)
                    throw new UnitTypeMismatchException(name, eu.Type, unit.Type);
                return _session.Run(n => n.ParameterUnitsAssign(_model, name, unit));
            },
            failProps: () => new { model = _model.Name, name, unit_name = unit.Name },
            okProps: assigned => new { model = _model.Name, name, unit_name = unit.Name, unit_type = unit.Type.ToString(), assigned });

    // 跨 model 守卫:unit.Model 必须等于当前 parameter 的 owner model。
    private void EnsureUnitBelongsToModel(CreoUnit unit)
    {
        if (unit.ModelName != _model.Name || unit.ModelType != _model.Type)
            throw new CrossModelUnitException(unit.ModelName, _model.Name);
    }

    // 参数名校验:空/超 ProName 容量提前拦截(对齐 CreoLayers.ValidateName),避免透传给 marshaller 静默截断。
    private static void ValidateParameterName(string name)
    {
        ThrowUtil.IfNullOrEmpty(name);
        if (name.Length >= CtkAbiConstants.ProNameSize)
            throw new ArgumentOutOfRangeException(nameof(name), "参数名不能超过 ProName 容量。");
    }
}

/// <summary>参数过滤条件(值过滤, 作用于已取回的快照): 名前缀 + 类型, 均可选。</summary>
/// <param name="NamePrefix">名前缀(序数比较); null 表示不按名过滤。</param>
/// <param name="Kind">值类型; null 表示不按类型过滤。</param>
public readonly record struct ParameterQuery(string? NamePrefix = null, CreoParamValueKind? Kind = null)
{
    /// <summary>无任何过滤条件。</summary>
    public bool IsEmpty => NamePrefix is null && Kind is null;

    /// <summary>判断参数是否命中本条件。</summary>
    public bool Matches(CreoParameter p)
    {
        if (NamePrefix is { } prefix && !p.Name.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        if (Kind is { } k && p.Value.Kind != k)
            return false;
        return true;
    }
}
