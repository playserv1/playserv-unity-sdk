using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Playserv.Deploy.Editor.Analysis
{
    internal sealed class DependencyGraph
    {
        private readonly HashSet<SyntaxNode> _typeNodes = new HashSet<SyntaxNode>();

        public void AddTypeNode(SyntaxNode typeNode)
        {
            _typeNodes.Add(typeNode);
        }

        public IEnumerable<SyntaxNode> GetAllDependentNodes()
        {
            return _typeNodes;
        }
    }
}
