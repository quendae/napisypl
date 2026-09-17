namespace NapisyPL.Core.Diagnostics;

public sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();

    private NullAppLogger() { }

    public void Info(string eventName, params (string Key, object? Value)[] fields) { }
    public void Error(string eventName, params (string Key, object? Value)[] fields) { }
}
