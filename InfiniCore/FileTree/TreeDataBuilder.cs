namespace InfiniCore.FileTree
{
    public static class TreeDataBuilder
    {
        public static Dictionary<string, Dictionary<string, object>> BuildTree(Dictionary<string, FileTreeNode> data)
        {
            return data.ToDictionary(x => x.Key, x => BuildNode(x.Key, x.Value));
        }

        private static Dictionary<string, object> BuildNode(string name, FileTreeNode data)
        {
            var result = new Dictionary<string, object>()
            {
                { "type", data.NodeType == FileTreeNodeType.File ? "file" : "folder" }
            };

            if (data.NodeType == FileTreeNodeType.Folder)
            {
                result.Add("empty", data.Empty);
            }

            if (data.NameOverride is not null)
            {
                result.Add("name_override", data.NameOverride);
            }

            return result;
        }
    }
}
