using System.Drawing;

namespace KJX.Devices;

public interface IImageBuffer : IDisposable
{
    public byte [] Buffer { get; }
    public Size Resolution { get; }
}