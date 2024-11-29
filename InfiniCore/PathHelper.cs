namespace InfiniCore
{
    public static class PathHelper
    {
        private static string rootDirectory = "";
        private static string pageDirectory = "";

        public static void SetRootDirectory(string root)
        {
            rootDirectory = root;
        }

        public static void SetPageDirectory(string page)
        {
            pageDirectory = page; 
        }

        public static string GetRootDirectory()
        {
            return rootDirectory;
        }

        public static string GetPageDirectory()
        {
            return pageDirectory;
        }

        public static string GetFilePath(string path)
        {
            return Path.Combine(rootDirectory, path);
        }

        public static string GetPageAssetPath(string path)
        {
            return Path.Combine(pageDirectory, path);
        }
    }
}
