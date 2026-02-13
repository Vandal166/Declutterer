using System;
using System.Threading.Tasks;

namespace Declutterer.Abstractions;

/// <summary>
/// Abstraction for UI thread dispatcher to enable testability.
/// Allows ViewModels to perform UI thread work without direct Avalonia dependency.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Executes an action on the UI thread asynchronously.
    /// </summary>
    /// <param name="action">The action to execute on the UI thread</param>
    /// <returns>A task that completes when the action finishes</returns>
    Task InvokeAsync(Action action);
    
    /// <summary>
    /// Executes a function on the UI thread asynchronously and returns its result.
    /// </summary>
    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="func">The function to execute on the UI thread</param>
    /// <returns>A task that completes with the function's return value</returns>
    Task<T> InvokeAsync<T>(Func<T> func);
}