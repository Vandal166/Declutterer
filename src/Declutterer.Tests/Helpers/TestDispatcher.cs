using Declutterer.Abstractions;

namespace Declutterer.Tests.Helpers;

/// <summary>
/// Test implementation of IDispatcher that executes synchronously for testing.
/// This eliminates threading complexity in unit tests.
/// </summary>
public class TestDispatcher : IDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var result = func();
        return Task.FromResult(result);
    }
}
