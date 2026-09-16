namespace CreoToolkit.Sdk.Diagnostics;

internal static class CreoUnitGuard
{
    internal static void NotUninitialized(CreoUnit unit, string paramName)
    {
        if (unit.IsUninitialized)
            throw new InvalidOperationException(
                $"CreoUnit '{paramName}' 是 default 值,只能经 CreoModel.UnitInit/UnitFromExpression 创建。");
    }
}