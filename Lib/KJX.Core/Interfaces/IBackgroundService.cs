namespace KJX.Core.Interfaces;

/// <summary>
/// Interface for services running in the background (i.e. sensor monitoring, temperature monitoring, etc.).
/// </summary>
public interface IBackgroundService
{
    void Start();
}