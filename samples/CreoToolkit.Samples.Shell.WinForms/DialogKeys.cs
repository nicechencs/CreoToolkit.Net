namespace CreoToolkit.Samples.Shell.WinForms;

// 7 个 API 主题 dialog 的稳定 key 常量, Catalog/DefaultFormFactory/ProtkSimpleSamplesApp 共享.
public static class DialogKeys
{
    public const string BasicSession    = "ctk.dialog.basic-session";
    public const string ModelDatabase   = "ctk.dialog.model-database";
    public const string FeatureGeom     = "ctk.dialog.feature-geom";
    public const string ParamsUnits     = "ctk.dialog.params-units";
    public const string DimDrawing      = "ctk.dialog.dim-drawing";
    public const string AsmSelect       = "ctk.dialog.asm-select";
    public const string DiagDemo        = "ctk.dialog.diag-demo";

    public static readonly IReadOnlyList<string> All = new[]
    {
        BasicSession, ModelDatabase, FeatureGeom, ParamsUnits, DimDrawing, AsmSelect, DiagDemo,
    };
}
