using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using IDispatcher = Declutterer.Abstractions.IDispatcher;

namespace Declutterer.Services;

public sealed class AvaloniaDispatcher : IDispatcher
{
    public async Task InvokeAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }
    
    public async Task<T> InvokeAsync<T>(Func<T> func)
    {
        return await Dispatcher.UIThread.InvokeAsync(func);
    }
}