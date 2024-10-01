using MaskCreator.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MaskCreator.Masks
{
    public class PointMaskLayerData(string name, int width, int height) : MaskLayerData(name, width, height)
    {
        public override void CreateControlObject_MouseDown(int x, int y, MouseButton mb, Action overlayCallback)
        {
            if (mb == MouseButton.Left)
            {
                // Add positive point
                ControlObjects.Add(new ControlPoint(x, y, true));
            }
            else if (mb == MouseButton.Right)
            {
                // Add negative point
                ControlObjects.Add(new ControlPoint(x, y, false));
            }

            overlayCallback.Invoke(); // Update Overlay
        }

        public override void RemoveControlObject(ControlObject obj, Action callback)
        {
            ControlObjects.Remove(obj);

            callback.Invoke();
        }

        public ControlPoint[] GetControlPoints()
        {
            return ControlObjects.Where(x => x is ControlPoint).Select(x => (ControlPoint) x).ToArray();
        }

        public override BitmapSource RenderOverlayImage()
        {
            var positiveBrush = new SolidBrush(Color.Lime);
            var negativeBrush = new SolidBrush(Color.HotPink);

            using Image<Rgba32> image = new(BaseImageWidth, BaseImageHeight);

            // Draw control points
            image.Mutate(i => {
                foreach (var point in GetControlPoints())
                {
                    i.Fill(point.Label ? positiveBrush : negativeBrush, new EllipsePolygon(point.X, point.Y, 15F));
                }
            });

            return ImageProcessUtil.ConvertImageToBitmapSource(image);
        }

        public override ImageMaskLayerData? ConvertToImageLayer()
        {
            var bytesToUse = GetSelectedMaskBytes();

            if (bytesToUse is not null)
            {
                return ImageMaskLayerData.Convert("Image MaskLayer", BaseImageWidth, BaseImageHeight, bytesToUse);
            }
            
            return null;
        }
    }
}
