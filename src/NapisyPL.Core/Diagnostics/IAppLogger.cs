namespace NapisyPL.Core.Diagnostics;

public interface IAppLogger
{
    void Info(string eventName, params (string Key, object? Value)[] fields);
    void Error(string eventName, params (string Key, object? Value)[] fields);
}
