using MaskCreator.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MaskCreator.Masks
{
    public class ImageMaskLayerData(string name, int width, int height) : MaskLayerData(name, width, height)
    {
        public override void CreateControlObject_MouseDown(int x, int y, MouseButton mb, Action callback)
        {
            callback.Invoke();
        }

        public override void RemoveControlObject(ControlObject obj, Action callback)
        {
            ControlObjects.Remove(obj);

            callback.Invoke();
        }

        public override BitmapSource RenderOverlayImage()
        {
            using Image<Rgba32> image = new(BaseImageWidth, BaseImageHeight);

            return ImageProcessUtil.ConvertImageToBitmapSource(image);
        }

        public override ImageMaskLayerData? ConvertToImageLayer()
        {
            return null; // Cannot convert ImagerMaskLayer to ImagerMaskLayer
        }

        public static ImageMaskLayerData Convert(string name, int width, int height, byte[] data)
        {
            var converted = new ImageMaskLayerData(name, width, height)
            {
                // Only preserve mask data of one mask
                maskBytesData = [data]
            };

            return converted;
        }
    }
}