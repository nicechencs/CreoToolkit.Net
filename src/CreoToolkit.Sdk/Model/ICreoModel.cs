namespace CreoToolkit.Sdk;

/// <summary>Model 类型契约,对偶 <see cref="ICreoModelItem"/>。极简只暴 <see cref="Type"/>。
/// 标识(Name)等 L2 ModelIdentity 字段已在 <see cref="CreoModel"/> 基类暴露,接口不重复。</summary>
public interface ICreoModel
{
    /// <summary>模型类型(对应 ProMdlfileType)。</summary>
    CreoModelType Type { get; }
}
