using System.Drawing;
using System.Runtime.Versioning;

namespace KJX.Devices;

/// <summary>
/// Interface for a generic camera implementation.
/// </summary>
public interface ICamera : IDevice
{
    public Size[] SupportedResolutions();
    public Size Resolution { get; set; }
    public IImageBuffer GetImage();
    public string Name { get; }
}