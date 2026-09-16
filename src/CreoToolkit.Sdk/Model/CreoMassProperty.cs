namespace CreoToolkit.Sdk;

/// <summary>实体模型质量属性(ProSolidMassPropertyGet 的 DATA 快照,v1:7 个关键标量)。</summary>
public readonly record struct CreoMassProperty(
    double Volume, double SurfaceArea, double Density, double Mass,
    double CenterOfGravityX, double CenterOfGravityY, double CenterOfGravityZ);
