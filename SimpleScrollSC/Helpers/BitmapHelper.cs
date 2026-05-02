using ScrollShot.Core;
using System.Drawing;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScrollShot.Helpers;

public enum ImageFormat
{
    Png,
    Jpeg
}

public static class BitmapHelper
{
    public static CapturedFrame Crop(CapturedFrame frame, Rectangle crop)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new ArgumentException("Frame is empty.", nameof(frame));
        }

        Rectangle bounds = new(0, 0, frame.Width, frame.Height);
        Rectangle clipped = Rectangle.Intersect(bounds, crop);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            throw new ArgumentException("Crop rectangle is outside the frame.", nameof(crop));
        }

        int outStride = clipped.Width * 4;
        byte[] output = new byte[outStride * clipped.Height];

        for (int row = 0; row < clipped.Height; row++)
        {
            int srcOffset = ((clipped.Y + row) * frame.Stride) + (clipped.X * 4);
            int dstOffset = row * outStride;
            Buffer.BlockCopy(frame.Pixels, srcOffset, output, dstOffset, outStride);
        }

        return new CapturedFrame(clipped.Width, clipped.Height, output);
    }

    public static CapturedFrame FromHBitmap(IntPtr hBitmap, bool applyHdrColorConversion)
    {
        BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap,
            IntPtr.Zero,
            System.Windows.Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int stride = bgra.PixelWidth * 4;
        byte[] pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        if (applyHdrColorConversion)
        {
            ToneMapHdrToSdrInPlace(pixels);
        }

        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        return new CapturedFrame(bgra.PixelWidth, bgra.PixelHeight, pixels);
    }

    private static void ToneMapHdrToSdrInPlace(byte[] bgraPixels)
    {
        // Best-effort HDR->SDR: compress highlights and restore perceived contrast.
        // Runs ONLY when HDR is enabled on the monitor (see HdrInfo).
        const double exposure = 0.85;

        for (int i = 0; i < bgraPixels.Length; i += 4)
        {
            double b = SrgbToLinear(bgraPixels[i] / 255.0);
            double g = SrgbToLinear(bgraPixels[i + 1] / 255.0);
            double r = SrgbToLinear(bgraPixels[i + 2] / 255.0);

            r *= exposure;
            g *= exposure;
            b *= exposure;

            r = Reinhard(r);
            g = Reinhard(g);
            b = Reinhard(b);

            bgraPixels[i] = (byte)Math.Clamp((int)Math.Round(LinearToSrgb(b) * 255.0), 0, 255);
            bgraPixels[i + 1] = (byte)Math.Clamp((int)Math.Round(LinearToSrgb(g) * 255.0), 0, 255);
            bgraPixels[i + 2] = (byte)Math.Clamp((int)Math.Round(LinearToSrgb(r) * 255.0), 0, 255);
        }
    }

    private static double Reinhard(double x) => x / (1.0 + x);

    private static double SrgbToLinear(double c)
    {
        if (c <= 0.04045)
        {
            return c / 12.92;
        }

        return Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double LinearToSrgb(double c)
    {
        c = Math.Clamp(c, 0.0, 1.0);
        if (c <= 0.0031308)
        {
            return 12.92 * c;
        }

        return 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
    }

    public static double AverageDifference(CapturedFrame left, CapturedFrame right)
    {
        if (left.Width != right.Width || left.Height != right.Height)
        {
            return double.MaxValue;
        }

        int xStep = Math.Max(1, left.Width / 128);
        int yStep = Math.Max(1, left.Height / 128);
        long total = 0;
        long samples = 0;

        for (int y = 0; y < left.Height; y += yStep)
        {
            int row = y * left.Stride;
            for (int x = 0; x < left.Width; x += xStep)
            {
                int index = row + (x * 4);
                total += Math.Abs(left.Pixels[index] - right.Pixels[index]);
                total += Math.Abs(left.Pixels[index + 1] - right.Pixels[index + 1]);
                total += Math.Abs(left.Pixels[index + 2] - right.Pixels[index + 2]);
                samples += 3;
            }
        }

        return samples == 0 ? double.MaxValue : total / (double)samples;
    }

    public static void Save(CapturedFrame frame, string path, ImageFormat format, int jpegQuality)
    {
        BitmapEncoder encoder = format == ImageFormat.Jpeg
            ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) }
            : new PngBitmapEncoder();

        encoder.Frames.Add(BitmapFrame.Create(frame.ToBitmapSource()));

        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }
}
