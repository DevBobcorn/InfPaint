namespace InfiniCore.FileTree
{
    public enum FileTreeNodeType
    {
        File,
        Folder
    }

    public record FileTreeNode
    {
        public FileTreeNodeType NodeType;
        public string Path;
        public string? NameOverride;

        // Only used if type is folder
        public bool Empty;

        public FileTreeNode(FileTreeNodeType nodeType, string path, string? nameOverride, bool empty)
        {
            NodeType = nodeType;
            Path = path;
            NameOverride = nameOverride;
            Empty = empty;
        }
    }
}
