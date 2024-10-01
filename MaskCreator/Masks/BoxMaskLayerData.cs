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
    public class BoxMaskLayerData(string name, int width, int height) : MaskLayerData(name, width, height)
    {
        // Used only during creation of a box
        private (int X, int Y)? boxCreationPointA = null;
        private (int X, int Y)? boxCreationPointB = null;

        public BoxMaskLayerData(string name, int width, int height, int x1, int y1, int x2, int y2) : this(name, width, height)
        {
            ControlObjects.Add(new ControlBox(x1, y1, x2, y2));
        }

        public override void CreateControlObject_MouseDown(int x, int y, MouseButton mb, Action overlayCallback)
        {
            if (GetControlBox() is null) // Define the box
            {
                boxCreationPointA = (x, y);
                //boxCreationPointB = (x, y);
            }
            else // Box already defined, add points
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
            }

            overlayCallback.Invoke();
        }

        public override void CreateControlObject_MouseMove(int x, int y, Action overlayCallback)
        {
            if (boxCreationPointA is not null)
            {
                boxCreationPointB = (x, y);

                // Redraw only if box creation is set
                overlayCallback.Invoke();
            }
        }

        public override void CreateControlObject_MouseUp(int x, int y, MouseButton mb, Action overlayCallback)
        {
            if (boxCreationPointA is not null)
            {
                // Add positive point
                ControlObjects.Add(new ControlBox(boxCreationPointA.Value.X, boxCreationPointA.Value.Y, x, y));
            }

            boxCreationPointA = null;
            boxCreationPointB = null;

            overlayCallback.Invoke();
        }

        public override void RemoveControlObject(ControlObject obj, Action callback)
        {
            ControlObjects.Remove(obj);

            callback.Invoke();
        }

        // Only one box is allowed
        public ControlBox? GetControlBox()
        {
            return (ControlBox?) ControlObjects.FirstOrDefault(x => x is ControlBox);
        }

        public ControlPoint[] GetControlPoints()
        {
            return ControlObjects.Where(x => x is ControlPoint).Select(x => (ControlPoint) x).ToArray();
        }

        public override BitmapSource RenderOverlayImage()
        {
            var positiveBrush = new SolidBrush(Color.Lime);
            var negativeBrush = new SolidBrush(Color.HotPink);
            var boxBrush = new SolidBrush(Color.Orange);
            var boxPen = new SolidPen(boxBrush, 5);

            using Image<Rgba32> image = new(BaseImageWidth, BaseImageHeight);

            var box = GetControlBox();

            // Draw control points
            image.Mutate(i => {
                foreach (var point in GetControlPoints())
                {
                    i.Fill(point.Label ? positiveBrush : negativeBrush, new EllipsePolygon(point.X, point.Y, 15F));
                }
            });

            // Draw control box
            if (box is not null)
            {
                image.Mutate(i => {
                    i.Draw(boxPen, new Rectangle(box.X1, box.Y1, box.X2 - box.X1, box.Y2 - box.Y1));
                });
            }
            else if (boxCreationPointA is not null && boxCreationPointB is not null)
            {
                image.Mutate(i => {
                    int cX1 = Math.Min(boxCreationPointA.Value.X, boxCreationPointB.Value.X);
                    int cY1 = Math.Min(boxCreationPointA.Value.Y, boxCreationPointB.Value.Y);
                    int cX2 = Math.Max(boxCreationPointA.Value.X, boxCreationPointB.Value.X);
                    int cY2 = Math.Max(boxCreationPointA.Value.Y, boxCreationPointB.Value.Y);

                    i.Draw(boxPen, new Rectangle(cX1, cY1, cX2 - cX1, cY2 - cY1));
                });
            }

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
