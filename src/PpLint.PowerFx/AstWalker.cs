using Microsoft.PowerFx.Syntax;

namespace PpLint.PowerFx;

/// <summary>
/// Percurso da AST do Power Fx. Um visitor coleta cada nó visitado, de modo
/// que as regras trabalhem sobre uma sequência simples em vez de escrever
/// travessia própria.
/// </summary>
public static class AstWalker
{
    public static IEnumerable<TexlNode> Descendants(TexlNode root)
    {
        var collector = new NodeCollector();
        root.Accept(collector);
        return collector.Nodes;
    }

    public static IEnumerable<CallNode> Calls(TexlNode root, string functionName) =>
        Descendants(root)
            .OfType<CallNode>()
            .Where(c => string.Equals(FunctionName(c), functionName, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<FirstNameNode> Identifiers(TexlNode root) =>
        Descendants(root).OfType<FirstNameNode>();

    public static string? FunctionName(CallNode call) => call.Head?.Name.Value;

    private sealed class NodeCollector : IdentityTexlVisitor
    {
        public List<TexlNode> Nodes { get; } = [];

        public override void Visit(ErrorNode node) => Add(node);
        public override void Visit(BlankNode node) => Add(node);
        public override void Visit(BoolLitNode node) => Add(node);
        public override void Visit(StrLitNode node) => Add(node);
        public override void Visit(NumLitNode node) => Add(node);
        public override void Visit(DecLitNode node) => Add(node);
        public override void Visit(FirstNameNode node) => Add(node);
        public override void Visit(ParentNode node) => Add(node);
        public override void Visit(SelfNode node) => Add(node);

        public override void PostVisit(DottedNameNode node) => Add(node);
        public override void PostVisit(UnaryOpNode node) => Add(node);
        public override void PostVisit(BinaryOpNode node) => Add(node);
        public override void PostVisit(VariadicOpNode node) => Add(node);
        public override void PostVisit(CallNode node) => Add(node);
        public override void PostVisit(ListNode node) => Add(node);
        public override void PostVisit(RecordNode node) => Add(node);
        public override void PostVisit(TableNode node) => Add(node);
        public override void PostVisit(AsNode node) => Add(node);
        public override void PostVisit(StrInterpNode node) => Add(node);

        private void Add(TexlNode node) => Nodes.Add(node);
    }
}
