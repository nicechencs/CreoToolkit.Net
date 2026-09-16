namespace CreoToolkit.Sdk;

/// <summary>Cable 单段末端 boundary(<c>ProCableLocationsOnSegEndGet</c> 返回元素)。
/// <para>
/// 每 pair = ProCablelocation[2] = <c>struct pro_model_item[2]</c>,SDK 快照拆成两
/// <see cref="CableBoundaryItem"/>(Start / End)。<br/>
/// <b>所有权语义</b>: PTC 头文件与手册均未明写 free 契约(同文件邻近函数全都明写
/// ProArrayFree),Bridge 内保守 borrowed 语义,raw ProArray 句柄不外露以防误 free。
/// public 查询入口带 <c>CTKEXP001</c> 实验诊断；高频调用可能累积 native 内存，
/// PTC 书面澄清或专用真机压力门禁确认前不得猜测 free 契约。
/// </para></summary>
public sealed record CableSegmentBoundary(CableBoundaryItem Start, CableBoundaryItem End);

/// <summary>Cable boundary 单个 model item 数据快照(pro_model_item 中 type + id 两主字段)。
/// TypeCode = <c>pro_obj_types</c> 枚举 int(常见值:PRO_CABLE_LOCATION=504)。</summary>
public sealed record CableBoundaryItem(int TypeCode, int Id);
