using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BitMiracle.LibTiff.Classic;
using KJX.ProjectTemplate.Devices;

namespace Devices.Utils;

public static class ImageUtils
{
    public static IImage ConvertImage(this IImageBuffer img)
    {
        int width = (int)img.Resolution.Width;
        int height = (int)img.Resolution.Height;


        using (var ms = new MemoryStream())
        {
            using (var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                       Avalonia.Platform.PixelFormat.Bgra8888, AlphaFormat.Unpremul))
            {
                using (var lockedBitmap = bitmap.Lock())
                {
                    unsafe
                    {
                        byte* buffer = (byte*)lockedBitmap.Address.ToPointer();

                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int pixelIndex = y * width + x;
                                byte grayValue = img.Buffer[pixelIndex];

                                buffer[pixelIndex * 4 + 0] = grayValue; // Blue
                                buffer[pixelIndex * 4 + 1] = grayValue; // Green
                                buffer[pixelIndex * 4 + 2] = grayValue; // Red
                                buffer[pixelIndex * 4 + 3] = 255; // Alpha
                            }
                        }
                    }
                }

                bitmap.Save(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return new Avalonia.Media.Imaging.Bitmap(ms);
            }
        }
    }

    public static IImageBuffer LoadFromResource(Assembly assembly, string resourceName)
    {
        using var resourceStream = assembly.GetManifestResourceStream(resourceName);
        // read a TIFF image from the open stream
        using (Tiff input = Tiff.ClientOpen("in-memory", "r", resourceStream, new TiffStream()))
        {
            if (input == null)
            {
                throw new IOException("Could not open the input file.");
            }

            var fullImageWidth = input.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            var fullImageHeight = input.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            var fullImage = new byte[fullImageWidth * fullImageHeight];
            var scanLine = new byte[input.ScanlineSize()];
            for (int i = 0; i < fullImageHeight; i++)
            {
                input.ReadScanline(scanLine, i, 0);
                Array.Copy(scanLine, 0, fullImage, i * fullImageWidth, fullImageWidth);
            }

            return new ManagedImageBuffer()
                { Buffer = fullImage, Resolution = new System.Drawing.Size(fullImageWidth, fullImageHeight) };
        }

    }
}