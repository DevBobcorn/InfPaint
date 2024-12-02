namespace InfiniCore.ImageHandle
{
    public class MaskImageHelper
    {
        public static string GetMaskImagePath(string fileServerRoot, string sourcePath)
        {
            if (Directory.Exists(fileServerRoot) && sourcePath.Contains('.'))
            {
                return fileServerRoot + sourcePath[..sourcePath.LastIndexOf('.')] + "_mask.png";
            }

            return string.Empty;
        }

        public static bool StoreMaskImagePath(string fileServerRoot, string sourcePath, byte[] bytes)
        {
            if (Directory.Exists(fileServerRoot) && sourcePath.Contains('.'))
            {
                var savedMaskPath = fileServerRoot + sourcePath[..sourcePath.LastIndexOf('.')] + "_mask.png";

                try
                {
                    File.WriteAllBytes(savedMaskPath, bytes);

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An exception occurred when storing mask image: {ex}");
                    return false;
                }
            }

            return false;
        }

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

            return [];
        }
    }
}