namespace MaskCreator.Masks
{
    public class ControlPoint(int x, int y, bool label) : ControlObject
    {
        public override string Description => $"Point {X}, {Y} [{(Label ? "+" : "-")}]";

        public readonly int X = x;
        public readonly int Y = y;
        public readonly bool Label = label;
    }
}
