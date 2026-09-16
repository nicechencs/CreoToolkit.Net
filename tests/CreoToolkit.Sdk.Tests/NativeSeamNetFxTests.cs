using System.Linq;
using System.Reflection;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Native;
using Xunit;

namespace CreoToolkit.Sdk.Tests;

public sealed class NativeSeamNetFxTests
{
    [Fact]
    public void ICreoNative_has_no_default_interface_method_bodies()
    {
        var withBodies = typeof(ICreoNative)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(ICreoNative) && m.GetMethodBody() != null)
            .Select(m => m.Name)
            .ToArray();
        Assert.Empty(withBodies);
    }

    [Fact]
    public void ICreoNative_declares_former_dim_member_ParameterGetWithUnits()
    {
        var method = typeof(ICreoNative).GetMethod(nameof(ICreoNative.ParameterGetWithUnits));
        Assert.NotNull(method);
        Assert.Null(method!.GetMethodBody());
    }

    [Fact]
    public void ProErrorPolicy_classifies_query_lookup_without_FrozenDictionary()
    {
        Assert.Equal(CreoOutcome.ConditionNegative, ProErrorPolicy.Classify("ProMdlCurrentGet", ProError.NotFound));
        Assert.Equal(CreoOutcome.Success, ProErrorPolicy.Classify("ProMdlCurrentGet", ProError.NoError));
        Assert.DoesNotContain("FrozenDictionary", typeof(ProErrorPolicy).Assembly.GetTypes().Select(t => t.Name));
    }
}
