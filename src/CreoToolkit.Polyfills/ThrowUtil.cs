#if NETFRAMEWORK
#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>net472 stand-ins for net6+ ArgumentNullException.ThrowIf* helpers.</summary>
    public static class ThrowUtil
    {
        public static void IfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null)
                throw new ArgumentNullException(paramName);
        }

        public static void IfNullOrWhiteSpace([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null || argument.Trim().Length == 0)
                throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

        public static void IfNullOrEmpty([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null || argument.Length == 0)
                throw new ArgumentException("Value cannot be null or empty.", paramName);
        }

        public static void IfDisposed([DoesNotReturnIf(true)] bool condition, object instance)
        {
            if (condition)
                throw new ObjectDisposedException(instance?.GetType().FullName);
        }

        public static void IfNegative(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(paramName, value, "Value must be non-negative.");
        }

        public static void IfNegativeOrZero(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(paramName, value, "Value must be greater than zero.");
        }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class DoesNotReturnIfAttribute : Attribute
    {
        public DoesNotReturnIfAttribute(bool parameterValue) => ParameterValue = parameterValue;

        public bool ParameterValue { get; }
    }
}
#endif
