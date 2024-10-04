using System.Globalization;
using System.Reflection;
using Autofac.Features.AttributeFilters;
using Autofac.Features.Metadata;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Devices.Utils;
using Framework.Devices.Logic;


namespace Framework.Devices;

public class SimulatedCamera : SupportsInitialization, ICamera
{
    public string Name { get; }
    int lastImageIndex = 0;

    private IImageBuffer[] _images;
    private readonly IMotor _xMotor;
    private readonly IMotor _yMotor;
    private readonly IMotor _zMotor;

    public SimulatedCamera(string name, [KeyFilter("XMotor")] IMotor xMotor, 
        [KeyFilter("YMotor")] IMotor yMotor,
        [KeyFilter("ZMotor")] IMotor zMotor)
    {
        Name = name;
        _images = new IImageBuffer[]
        {
            ImageUtils.LoadFromResource(Assembly.GetExecutingAssembly(), "Framework.Devices.Resources.blocks1.tiff"),
            ImageUtils.LoadFromResource(Assembly.GetExecutingAssembly(), "Framework.Devices.Resources.blocks2.tiff"),
        };
        _xMotor = xMotor;
        _yMotor = yMotor;
        _zMotor = zMotor;
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
        var index = (int)(Math.Abs(_xMotor.Position + _yMotor.Position)) % _images.Length;
        System.Diagnostics.Debug.WriteLine($"Image index: {index}");
        IImageBuffer result = new  ManagedImageBuffer() { Buffer = _images[index].Buffer.Clone() as byte[], Resolution = _images[index].Resolution };
        var blurs = Math.Min(10, Math.Abs(_zMotor.Position*10));
        for (var blur = 0; blur < blurs; blur++)
        {
            result = GaussianBlur(result);
        }

        return result;
    }
    
    private static IImageBuffer GaussianBlur(IImageBuffer image)
    {
        int width = image.Resolution.Width;
        int height = image.Resolution.Height;
        byte[] blurredImage = new byte[image.Buffer.Length];

        // Define a 5x5 Gaussian kernel with sigma = 1.0
        double[,] kernel = {
            { 1,  4,  7,  4, 1 },
            { 4, 16, 26, 16, 4 },
            { 7, 26, 41, 26, 7 },
            { 4, 16, 26, 16, 4 },
            { 1,  4,  7,  4, 1 }
        };

        double kernelSum = 273.0; // Sum of all kernel values

        // Apply the Gaussian kernel to each pixel in the image
        for (int y = 2; y < height - 2; y++)
        {
            for (int x = 2; x < width - 2; x++)
            {
                double sum = 0.0;

                for (int ky = -2; ky <= 2; ky++)
                {
                    for (int kx = -2; kx <= 2; kx++)
                    {
                        int pixel = image.Buffer[(y + ky) * width + (x + kx)];
                        sum += pixel * kernel[ky + 2, kx + 2];
                    }
                }

                blurredImage[y * width + x] = (byte)(sum / kernelSum);
            }
        }

        return new ManagedImageBuffer() { Buffer=blurredImage, Resolution = image.Resolution };
    }

    private ManagedImageBuffer CreateCheckeredBitmapWithText(string text)
    {
        // Define the dimensions of the bitmap
        int width = (int)Resolution.Width;
        int height = (int)Resolution.Height;
        const int checkerSize = 32;
        // Create a new RenderTargetBitmap
        using (var renderTargetBitmap = new RenderTargetBitmap(new PixelSize(width, height)))
        {
            // Draw the checkered background using a DrawingContext
            using (var context = renderTargetBitmap.CreateDrawingContext())
            {
                for (int x = 0; x < width; x += checkerSize)
                {
                    for (int y = 0; y < height; y += checkerSize)
                    {
                        IBrush color = (x / checkerSize + y / checkerSize) % 2 == 0 ? Brushes.White : Brushes.Black;
                        context.FillRectangle(color, new Rect(x, y, checkerSize, checkerSize));
                    }
                }

                // Draw the text
                // Draw the text using FormattedText
                var formattedText = new FormattedText(
                    text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    18, Brushes.Red);

                context.DrawText(formattedText, new Point(width/2 - formattedText.Width/2, height/2 - formattedText.Height/2));
            }
            return ConvertBitmapToGreyscale(renderTargetBitmap);
        }
    }

    ManagedImageBuffer ConvertBitmapToGreyscale(Avalonia.Media.Imaging.RenderTargetBitmap bitmap)
    {
        byte[] managedBuffer = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height];
        using (var writeableBitmap = new WriteableBitmap(
                   bitmap.PixelSize,
                   bitmap.Dpi,
                   bitmap.Format))
        {
            using var lockedFramebuffer = writeableBitmap.Lock();
            bitmap.CopyPixels(lockedFramebuffer, AlphaFormat.Unpremul);
            // Get the pointer to the buffer
            IntPtr buffer = lockedFramebuffer.Address;
            // Get the number of bytes per pixel
            int bytesPerPixel = (writeableBitmap.Format.Value.BitsPerPixel + 7) / 8;
            // Get the number of bytes per row
            int stride = writeableBitmap.PixelSize.Width * bytesPerPixel;
            // Get the number of bytes per pixel
            int width = writeableBitmap.PixelSize.Width;
            int height = writeableBitmap.PixelSize.Height;
            // Iterate over the pixels in the buffer
            int outputIndex = 0;
            unsafe
            {
                byte* p = (byte*)buffer;
                for (int i = 0; i < width * height; i++)
                {
                    // Get the pixel value
                    byte pixelValue = (byte)(0.299 * p[2] + 0.587 * p[1] + 0.114 * p[0]);
                    managedBuffer[outputIndex++] = pixelValue;
                    // Move to the next pixel
                    p += bytesPerPixel;
                }
            }
        }
        // Unlock the bitmap buffer
        return new ManagedImageBuffer { Buffer = managedBuffer, Resolution = new System.Drawing.Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height) };
    }
}
