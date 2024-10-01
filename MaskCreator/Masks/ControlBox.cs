namespace MaskCreator.Masks
{
    public class ControlBox(int x1, int y1, int x2, int y2) : ControlObject
    {
        public override string Description => $"Box {X1}, {Y1}, {X2}, {Y2} ({X2 - X1}x{Y2 - Y1})";

        public readonly int X1 = Math.Min(x1, x2);
        public readonly int Y1 = Math.Min(y1, y2);
        public readonly int X2 = Math.Max(x1, x2);
        public readonly int Y2 = Math.Max(y1, y2);
    }
}
