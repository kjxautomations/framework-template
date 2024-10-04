using System.Drawing;

namespace Framework.Devices;

public interface IImageBuffer : IDisposable
{
    public byte [] Buffer { get; }
    public Size Resolution { get; }
}