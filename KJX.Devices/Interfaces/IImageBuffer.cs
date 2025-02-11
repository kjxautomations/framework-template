using System.Drawing;

namespace KJX.Devices;

/// <summary>
/// Interface for images captured from an optical source and can be rendered within the UI.
/// </summary>
public interface IImageBuffer : IDisposable
{
    public byte [] Buffer { get; }
    public Size Resolution { get; }
}