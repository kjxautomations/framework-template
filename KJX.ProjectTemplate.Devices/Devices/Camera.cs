using KJX.ProjectTemplate.Devices;
using KJX.ProjectTemplate.Devices.Logic;


namespace Framework.Devices;

public class Camera : SupportsInitialization, ICamera
{
    public string Name { get; }

    public Camera(string name)
    {
        Name = name;
    }

    protected override void DoInitialize()
    {
        
    }

    public CameraProperties SupportedProperties => CameraProperties.Resolution;
    public int Exposure { get; set; }
    public int Gain { get; set; }
    public System.Drawing.Size[] SupportedResolutions()
    {
        return new[] { new System.Drawing.Size(640, 480) };
    }

    public System.Drawing.Size Resolution
    {
        get => new System.Drawing.Size(640, 480);
        set
        {
            if (value != Resolution)
                throw new ArgumentException("Resolution not supported");
        }
    }

    public IImageBuffer GetImage()
    {
        return new ManagedImageBuffer() { Buffer = new byte[640 * 480], Resolution = Resolution};
    }
}
