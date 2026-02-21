using System;

namespace Declutterer.Utilities.Exceptions;

/// <summary>
/// Custom exception for operation failures during file/directory deletion operations.
/// </summary>
public class OperationFailedException : Exception
{
    public OperationFailedException(string message) : base(message) { }
    public OperationFailedException(string message, Exception innerException) : base(message, innerException) { }
}