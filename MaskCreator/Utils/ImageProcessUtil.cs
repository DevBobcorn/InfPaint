using MaskCreator.Masks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using System.IO;

namespace MaskCreator.Utils
{
    public static class ImageProcessUtil
    {
        public static BitmapSource ByteArrayToBitmapSource(byte[] byteArray)
        {
            using (var stream = new MemoryStream(byteArray))
            {
                var bitmapImage = new BitmapImage();

                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad; // Set CacheOption after BeginInit
                bitmapImage.StreamSource = stream;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Optional: Freeze to make it cross-thread accessible

                return bitmapImage;
            }
        }

        public static BitmapSource LoadMaskAsBitmapSource(string maskPath, byte maskR, byte maskG, byte maskB)
        {
            using var maskImage = Image.Load<Rgba32>(maskPath);
            return ConvertMaskToBitmapSource(maskImage, maskR, maskG, maskB);
        }

        public static BitmapSource LoadMaskAsBitmapSource(byte[] maskBytes, byte maskR, byte maskG, byte maskB)
        {
            using var maskImage = Image.Load<Rgba32>(maskBytes);
            return ConvertMaskToBitmapSource(maskImage, maskR, maskG, maskB);
        }

        // https://github.com/SixLabors/ImageSharp/issues/531
        public static BitmapSource ConvertMaskToBitmapSource(Image<Rgba32> maskImage, byte maskR, byte maskG, byte maskB)
        {
            var converted = new WriteableBitmap(maskImage.Width, maskImage.Height, maskImage.Metadata.HorizontalResolution, maskImage.Metadata.VerticalResolution, PixelFormats.Bgra32, null);
            converted.Lock();

            try
            {
                maskImage.ProcessPixelRows(accessor =>
                {
                    var backBuffer = converted.BackBuffer;

                    for (var y = 0; y < maskImage.Height; y++)
                    {
                        Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

                        for (var x = 0; x < maskImage.Width; x++)
                        {
                            var backBufferPos = backBuffer + (y * maskImage.Width + x) * 4;
                            var rgba = pixelRow[x];
                            // Use grayscale as opacity, and tint with given RGB
                            int color = rgba.R << 24 | maskR << 16 | maskG << 8 | maskB;
                            
                            System.Runtime.InteropServices.Marshal.WriteInt32(backBufferPos, color);
                        }
                    }
                });

                converted.AddDirtyRect(new Int32Rect(0, 0, maskImage.Width, maskImage.Height));
            }
            finally
            {
                converted.Unlock();
            }

            return converted;
        }

        public static BitmapSource ConvertImageToBitmapSource(Image<Rgba32> inImage)
        {
            var converted = new WriteableBitmap(inImage.Width, inImage.Height, inImage.Metadata.HorizontalResolution, inImage.Metadata.VerticalResolution, PixelFormats.Bgra32, null);
            converted.Lock();

            try
            {
                inImage.ProcessPixelRows(accessor =>
                {
                    var backBuffer = converted.BackBuffer;

                    for (var y = 0; y < inImage.Height; y++)
                    {
                        Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

                        for (var x = 0; x < inImage.Width; x++)
                        {
                            var backBufferPos = backBuffer + (y * inImage.Width + x) * 4;
                            var rgba = pixelRow[x];
                            // Copy ARGB values
                            int color = rgba.A << 24 | rgba.R << 16 | rgba.G << 8 | rgba.B;

                            System.Runtime.InteropServices.Marshal.WriteInt32(backBufferPos, color);
                        }
                    }
                });

                converted.AddDirtyRect(new Int32Rect(0, 0, inImage.Width, inImage.Height));
            }
            finally
            {
                converted.Unlock();
            }

            return converted;
        }

        public static BitmapSource? CompositeMaskLayersAsBitmapSource(int width, int height,
                MaskLayerData[] maskLayers, byte maskR, byte maskG, byte maskB)
        {
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            using Image<Rgba32> composite = new(width, height);

            int maskComposited = 0;
            
            foreach (var layer in maskLayers)
            {
                var bytes = layer.GetSelectedMaskBytes();
                if (bytes is null) continue;

                using Image<Rgba32> mask = Image.Load<Rgba32>(bytes);

                if (mask.Width != width || mask.Height != height)
                {
                    // Resize mask to size of original image
                    mask.Mutate(i => i.Resize(width, height));
                }

                composite.Mutate(i =>
                {
                    i.DrawImage(mask, new GraphicsOptions { ColorBlendingMode = PixelColorBlendingMode.Add });
                });

                maskComposited += 1;
            }

            if (maskComposited == 0)
            {
                return null;
            }

            return ConvertMaskToBitmapSource(composite, maskR, maskG, maskB);
        }

        public static async Task<byte[]?> CompositeMaskLayersAsPngBytes(int width, int height,
                MaskLayerData[] maskLayers)
        {
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            using Image<Rgba32> composite = new(width, height);

            int maskComposited = 0;

            foreach (var layer in maskLayers)
            {
                var bytes = layer.GetSelectedMaskBytes();
                if (bytes is null) continue;

                using Image<Rgba32> mask = Image.Load<Rgba32>(bytes);

                if (mask.Width != width || mask.Height != height)
                {
                    // Resize mask to size of original image
                    mask.Mutate(i => i.Resize(width, height));
                }

                composite.Mutate(i =>
                {
                    i.DrawImage(mask, new GraphicsOptions { ColorBlendingMode = PixelColorBlendingMode.Add });
                });

                maskComposited += 1;
            }

            if (maskComposited == 0)
            {
                return null;
            }

            using var memoryStream = new MemoryStream();
            await composite.SaveAsPngAsync(memoryStream);

            return memoryStream.ToArray();
        }

    }
}
