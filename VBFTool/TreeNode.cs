using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VBFTool
{
    public class TreeNode
    {
        private string _fullPath;

        public TreeNode(string name, TreeNode parent = null)
        {
            Text = name;
            Parent = parent;
        }

        public TreeNode Parent { get; }

        public ObservableCollection<TreeNode> Nodes { get; } = new ObservableCollection<TreeNode>();

        public string Text { get; }

        public string FullPath
        {
            get
            {
                if (_fullPath != null)
                    return _fullPath;

                var tree = new List<TreeNode>();
                var parent = Parent;
                while (parent != null)
                {
                    tree.Add(parent);
                    parent = parent.Parent;
                }

                tree.Reverse();
                tree.Add(this);

                _fullPath = string.Join("/", tree);
                return _fullPath;
            }
        }

        public override string ToString() => Text;
    }
}