using System.Drawing;

namespace KJX.ProjectTemplate.Devices;

public interface IImageBuffer : IDisposable
{
    public byte [] Buffer { get; }
    public Size Resolution { get; }
}