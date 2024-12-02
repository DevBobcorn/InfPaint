using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace InfiniCore.ImageHandle
{
    public static class DummyImageHelper
    {
        private static byte[] EncodeImage(Image image, IImageEncoder encoder)
        {
            using (var ms = new MemoryStream())
            {
                image.Save(ms, encoder);
                return ms.ToArray();
            }
        }

        public static byte[] GetDummyPng(string text, int width, int height)
        {
            using var image = new Image<Rgba32>(width, height);
            using var imageProcessed = image.Clone(ctx => ctx.PrepareDummyImage(
                    text, Color.Black, Color.White));

            return EncodeImage(imageProcessed, new PngEncoder());
        }

        private static IImageProcessingContext PrepareDummyImage(this IImageProcessingContext processingContext,
            string text,
            Color foreground,
            Color background)
        {
            processingContext.Fill(background);

            var font = FontManager.Instance.GetFont("Earth Orbiter", 0.1F);

            if (font is not null)
            {
                processingContext.ApplyScalingWaterMark(font, text, foreground, 0F);
            }
            else
            {
                throw new FileNotFoundException("Font file cannot be found!");
            }

            return processingContext;
        }
    }
}
