using System.Drawing;
using KJX.Devices;

namespace KJX.Devices;

public class ManagedImageBuffer : IImageBuffer
{
    public void Dispose()
    {
    }

    public required byte[] Buffer { get; init; }
    public required Size Resolution { get; init; }
}