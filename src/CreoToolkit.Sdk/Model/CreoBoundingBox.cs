namespace CreoToolkit.Sdk;

/// <summary>实体模型的再生包围盒(ProSolidOutlineGet 的 DATA 快照,基坐标系)。</summary>
public readonly record struct CreoBoundingBox(
    double MinX, double MinY, double MinZ,
    double MaxX, double MaxY, double MaxZ);
