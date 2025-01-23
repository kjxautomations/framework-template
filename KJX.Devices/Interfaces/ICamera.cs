using System.Drawing;
using System.Runtime.Versioning;

namespace KJX.Devices;

[Flags]
public enum CameraProperties
{
    None = 0,
    Exposure = 1,
    Gain = 2,
    Resolution = 4
}



public interface ICamera : ISupportsInitialization
{
    public CameraProperties SupportedProperties { get; }
    public int Exposure { set; get; }
    public int Gain { get; set; }
    public Size[] SupportedResolutions();
    public Size Resolution { get; set; }
    public IImageBuffer GetImage();
    public string Name { get; }
}