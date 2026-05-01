using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScrollShot.Core;

public sealed class CapturedFrame
{
    public CapturedFrame(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Stride = width * 4;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] Pixels { get; }

    public BitmapSource ToBitmapSource()
    {
        BitmapSource source = BitmapSource.Create(
            Width,
            Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            Pixels,
            Stride);

        source.Freeze();
        return source;
    }
}
