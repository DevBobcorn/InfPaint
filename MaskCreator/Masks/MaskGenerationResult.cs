namespace MaskCreator.Masks
{
    public class MaskGenerationResult(byte[] b, float s)
    {
        public readonly byte[] bytes = b;
        public readonly float score = s;
    }
}
