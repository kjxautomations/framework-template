using KJX.Config;
using KJX.Devices.Logic;


namespace KJX.Devices;

public class Camera : DeviceBase, ICamera
{
    public string Name { get; }

    public Camera(string name)
    {
        Name = name;
    }

    protected override void DoInitialize()
    {
        
    }
    [RangeIncrement(0, 1000, 1)]
    [Group("Basic")]
    public int Exposure { get; set; }
    [RangeIncrement(0, 100, 1)]
    [Group("Basic")]
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
