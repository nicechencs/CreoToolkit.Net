using CreoToolkit.Sdk.Session;
using Xunit;

namespace CreoToolkit.Sdk.Tests;

public sealed class CreoThreadCaptureTests : IDisposable
{
    public CreoThreadCaptureTests() => CreoThread.Reset();

    [Fact]
    public void Repeated_capture_on_owner_thread_is_idempotent()
    {
        CreoThread.Capture();
        CreoThread.Capture();

        Assert.Equal(Environment.CurrentManagedThreadId, CreoThread.MainThreadId);
        Assert.True(CreoThread.IsMainThread);
    }

    [Fact]
    public void Another_thread_cannot_replace_the_owner()
    {
        CreoThread.Capture();
        var owner = CreoThread.MainThreadId;
        Exception? failure = null;
        var other = new Thread(() =>
        {
            try { CreoThread.Capture(); }
            catch (Exception ex) { failure = ex; }
        });

        other.Start();
        Assert.True(other.Join(TimeSpan.FromSeconds(5)));

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(owner, CreoThread.MainThreadId);
        Assert.True(CreoThread.IsMainThread);
    }

    public void Dispose() => CreoThread.Reset();
}
