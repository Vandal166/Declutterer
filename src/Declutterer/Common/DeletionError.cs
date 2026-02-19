using System;

namespace Declutterer.Common;

public sealed class DeletionError
{
    public string ItemPath { get; set; }
    public string ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
}