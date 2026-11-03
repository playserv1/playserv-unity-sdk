using System.Collections.Generic;

namespace Playserv.CodeGenerator.Editor
{
    internal sealed class SelectionNode
    {
        public string Name { get; }
        public List<SelectionNode> Children { get; }

        public SelectionNode(string name)
        {
            Name = name;
            Children = new List<SelectionNode>();
        }
    }
}