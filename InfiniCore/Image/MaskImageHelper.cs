namespace InfiniCore.ImageHandle
{
    public class MaskImageHelper
    {
        public static byte[] GetMaskImageBytes(string fileServerRoot, string sourcePath)
        {
            if (Directory.Exists(fileServerRoot) && sourcePath.Contains('.'))
            {
                var savedMaskPath = fileServerRoot + sourcePath[..sourcePath.LastIndexOf('.')] + "_mask.png";

                if (File.Exists(savedMaskPath))
                {
                    return File.ReadAllBytes(savedMaskPath);
                }
            }

            return Array.Empty<byte>();
        }
    }
}