using SixLabors.Fonts;

namespace InfiniCore.ImageHandle
{
    public class FontManager
    {
        public static readonly FontManager Instance = new();
        private FontManager() { }

        private readonly FontCollection collection = new();
        private bool initialized = false;

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                // Add font families to our collections...
                collection.Add(PathHelper.GetPageAssetPath("fonts/earthorbiter.ttf"));

                initialized = true;
            }
        }

        public Font? GetFont(string fontName, float fontSize = 12F, FontStyle fontStyle = FontStyle.Regular)
        {
            EnsureInitialized();

            if (collection.TryGet(fontName, out FontFamily family))
            {
                return family.CreateFont(fontSize, fontStyle);
            }

            return null;
        }
    }
}
