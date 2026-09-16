using CreoUnitType = CreoToolkit.Interop.Generated.ProUnitType;
using CreoUnitsystemType = CreoToolkit.Interop.Generated.ProUnitsystemType;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 带 owner 模型 + 名称 + 量纲的不可变单位快照(DATA copy-out)。
/// 不持有 native 句柄;native 调用时由模型名、模型类型和单位名重建。
/// </summary>
public readonly struct CreoUnit : IEquatable<CreoUnit>
{
    internal string ModelName { get; }

    internal CreoModelType ModelType { get; }

    /// <summary>单位名称。<c>default(CreoUnit)</c> 时为 null,作为未初始化哨兵。</summary>
    public string Name { get; }

    /// <summary>单位量纲类型,直接对齐 generated <c>ProUnitType</c>。</summary>
    public CreoUnitType Type { get; }

    internal CreoUnit(string modelName, CreoModelType modelType, string name, CreoUnitType type)
    {
        ModelName = modelName;
        ModelType = modelType;
        Name = name;
        Type = type;
    }

    /// <summary>是否为未初始化的 default 值。</summary>
    public bool IsUninitialized => Name is null;

    public bool Equals(CreoUnit other) =>
        ModelName == other.ModelName &&
        ModelType == other.ModelType &&
        Name == other.Name;

    public override bool Equals(object? obj) => obj is CreoUnit other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ModelName, ModelType, Name);

    public static bool operator ==(CreoUnit left, CreoUnit right) => left.Equals(right);

    public static bool operator !=(CreoUnit left, CreoUnit right) => !left.Equals(right);
}

/// <summary>单位线性换算参数。公式为 target = Offset + src * Scale。</summary>
public readonly record struct CreoUnitConversion(double Scale, double Offset)
{
    /// <summary>把源单位数值换算为目标单位数值。</summary>
    public double Apply(double src) => Offset + src * Scale;
}

/// <summary>带单位参数值的聚合快照(DATA)。<c>Unit == null</c> 表示参数无单位。</summary>
public readonly record struct CreoParameterValueWithUnits(
    string Name,
    double Value,
    CreoUnit? Unit,
    bool IsModified = false);

/// <summary>
/// 带 owner 模型 + 名称 + 类型的单位系统快照(DATA copy-out)。
/// 不持有 native 句柄;native 调用时由模型名、模型类型和单位系统名重建。
/// </summary>
public readonly struct CreoUnitSystem : IEquatable<CreoUnitSystem>
{
    internal string ModelName { get; }

    internal CreoModelType ModelType { get; }

    /// <summary>单位系统名称。<c>default(CreoUnitSystem)</c> 时为 null,作为未初始化哨兵。</summary>
    public string Name { get; }

    /// <summary>单位系统类型,直接对齐 generated <c>ProUnitsystemType</c>。</summary>
    public CreoUnitsystemType Type { get; }

    internal CreoUnitSystem(string modelName, CreoModelType modelType, string name, CreoUnitsystemType type)
    {
        ModelName = modelName;
        ModelType = modelType;
        Name = name;
        Type = type;
    }

    /// <summary>是否为未初始化的 default 值。</summary>
    public bool IsUninitialized => Name is null;

    public bool Equals(CreoUnitSystem other) =>
        ModelName == other.ModelName &&
        ModelType == other.ModelType &&
        Name == other.Name;

    public override bool Equals(object? obj) => obj is CreoUnitSystem other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ModelName, ModelType, Name);

    public static bool operator ==(CreoUnitSystem left, CreoUnitSystem right) => left.Equals(right);

    public static bool operator !=(CreoUnitSystem left, CreoUnitSystem right) => !left.Equals(right);
}
public static class CreoUnitExtensions
{
    /// <summary>计算从源单位到目标单位的线性换算参数。</summary>
    public static CreoUnitConversion ConvertTo(this CreoUnit from, CreoUnit to, CreoSession session)
    {
        ThrowUtil.IfNull(session);
        CreoUnitGuard.NotUninitialized(from, nameof(from));
        CreoUnitGuard.NotUninitialized(to, nameof(to));

        if (from.ModelName != to.ModelName || from.ModelType != to.ModelType)
            throw new CrossModelUnitException(from.ModelName, to.ModelName);

        if (from.Type != to.Type)
            throw new UnitTypeMismatchException("unit", from.Type, to.Type);

        var model = new ModelIdentity(from.ModelName, from.ModelType);
        var result = session.Run(n => n.UnitConvert(model, from.Name, to.Name));
        CreoSdkLog.Trace("unit", "convert",
            new
            {
                model = from.ModelName,
                from = from.Name,
                to = to.Name,
                type = from.Type.ToString(),
                scale = result.Scale,
                offset = result.Offset
            });
        return result;
    }
}
