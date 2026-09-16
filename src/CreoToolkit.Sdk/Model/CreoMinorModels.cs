using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>布局(Layout):占位,业务触发再扩。</summary>
public sealed class CreoLayout : CreoModel
{
    internal CreoLayout(CreoSession session, ModelIdentity id) : base(session, id) { }
}

/// <summary>格式(Format):占位,业务触发再扩。</summary>
public sealed class CreoFormat : CreoModel
{
    internal CreoFormat(CreoSession session, ModelIdentity id) : base(session, id) { }
}

/// <summary>图表(Diagram):占位,业务触发再扩。</summary>
public sealed class CreoDiagram : CreoModel
{
    internal CreoDiagram(CreoSession session, ModelIdentity id) : base(session, id) { }
}

/// <summary>标注(Markup):占位,业务触发再扩。</summary>
public sealed class CreoMarkup : CreoModel
{
    internal CreoMarkup(CreoSession session, ModelIdentity id) : base(session, id) { }
}

/// <summary>记事本(Notebook):占位,业务触发再扩。</summary>
public sealed class CreoNotebook : CreoModel
{
    internal CreoNotebook(CreoSession session, ModelIdentity id) : base(session, id) { }
}

/// <summary>线束模型(电气/管路布线;对齐 ProMdlfileType.HARNESS=11,对偶 old PtkHarness)。
/// 业务方法(Create/Subharnesses/Cables)留 future,等 Cable/Harness 模块切片触发。</summary>
public sealed class CreoHarness : CreoModel
{
    internal CreoHarness(CreoSession session, ModelIdentity id) : base(session, id) { }
}
