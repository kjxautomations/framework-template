using System.Drawing;
using KJX.ProjectTemplate.Devices;

namespace Framework.Devices;

public class ManagedImageBuffer : IImageBuffer
{
    public void Dispose()
    {
    }

    public required byte[] Buffer { get; init; }
    public required Size Resolution { get; init; }
}