using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Small_square_cavity_coating_machine.Services.Security;

public sealed class AvatarImageService : IAvatarImageService
{
    private const int MaximumDimension = 256;

    public byte[] LoadAndNormalize(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var input = File.OpenRead(filePath);
        var decoder = BitmapDecoder.Create(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault()
            ?? throw new InvalidOperationException("无法读取所选图片。");

        BitmapSource normalized = frame;
        var longestSide = Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (longestSide > MaximumDimension)
        {
            var scale = (double)MaximumDimension / longestSide;
            normalized = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(normalized));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
