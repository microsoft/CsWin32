// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private class WhitespaceRewriter : CSharpSyntaxRewriter
    {
        private readonly List<SyntaxTrivia> indentationLevels = new List<SyntaxTrivia> { default };
        private int indentationLevel;

        internal WhitespaceRewriter()
            : base(visitIntoStructuredTrivia: true)
        {
        }

        private SyntaxTrivia IndentTrivia => this.indentationLevels[this.indentationLevel];

        private SyntaxTrivia OuterIndentTrivia => this.indentationLevels[Math.Max(0, this.indentationLevel - 1)];

        public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            node = node
                .WithNamespaceKeyword(node.NamespaceKeyword.WithLeadingTrivia(TriviaList(this.IndentTrivia)))
                .WithOpenBraceToken(node.OpenBraceToken.WithLeadingTrivia(TriviaList(this.IndentTrivia)))
                .WithCloseBraceToken(node.CloseBraceToken.WithLeadingTrivia(TriviaList(this.IndentTrivia)));
            using var indent = new Indent(this);
            SyntaxNode? result = base.VisitNamespaceDeclaration(node);
            if (result is NamespaceDeclarationSyntax ns)
            {
                result = ns.WithMembers(AddSpacingBetweenMembers(ns.Members, ns.Usings.Count > 0));
            }

            return result;
        }

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        {
            node = this.WithIndentingTrivia(node)
                .WithOpenBraceToken(node.OpenBraceToken.WithLeadingTrivia(TriviaList(this.IndentTrivia)))
                .WithCloseBraceToken(node.CloseBraceToken.WithLeadingTrivia(TriviaList(this.IndentTrivia)));
            using var indent = new Indent(this);
            SyntaxNode? result = base.VisitStructDeclaration(node);
            if (result is StructDeclarationSyntax s)
            {
                result = s.WithMembers(AddSpacingBetweenMembers(s.Members));
            }

            return result;
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            node = this.WithIndentingTrivia(node)
                .WithOpenBraceToken(node.OpenBraceToken.WithLeadingTrivia(TriviaList(this.IndentTrivia)))
                .WithCloseBraceToken(node.CloseBraceToken.WithLeadingTrivia(TriviaList(this.IndentTrivia)));
            using var indent = new Indent(this);
            SyntaxNode? result = base.VisitClassDeclaration(node);
            if (result is ClassDeclarationSyntax c)
            {
                result = c.WithMembers(AddSpacingBetweenMembers(c.Members));
            }

            return result;
        }

        public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            node = this.WithIndentingTrivia(node)
                .WithOpenBraceToken(node.OpenBraceToken.WithLeadingTrivia(TriviaList(this.IndentTrivia)))
                .WithCloseBraceToken(node.CloseBraceToken.WithLeadingTrivia(TriviaList(this.IndentTrivia)));
            using var indent = new Indent(this);
            SyntaxNode? result = base.VisitInterfaceDeclaration(node);
            if (result is InterfaceDeclarationSyntax c)
            {
                result = c.WithMembers(AddSpacingBetweenMembers(c.Members));
            }

            return result;
        }

        public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            node = this.WithIndentingTrivia(node)
                .WithOpenBraceToken(node.OpenBraceToken.WithLeadingTrivia(TriviaList(this.IndentTrivia)))
                .WithCloseBraceToken(node.CloseBraceToken.WithLeadingTrivia(TriviaList(this.IndentTrivia)));
            using var indent = new Indent(this);
            return base.VisitEnumDeclaration(node);
        }

        public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
        {
            return base.VisitUsingDirective(node.WithLeadingTrivia(this.IndentTrivia));
        }

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            SyntaxTriviaList leadingTrivia;
            if (node.Parent is FixedStatementSyntax or AccessorDeclarationSyntax or TryStatementSyntax or FinallyClauseSyntax)
            {
                leadingTrivia = TriviaList(this.IndentTrivia);
            }
            else
            {
                leadingTrivia = TriviaList(LineFeed).Add(this.IndentTrivia);
            }

            node = node
                .WithOpenBraceToken(Token(leadingTrivia, SyntaxKind.OpenBraceToken, TriviaList(LineFeed)))
                .WithCloseBraceToken(Token(TriviaList(this.IndentTrivia), SyntaxKind.CloseBraceToken, TriviaList(LineFeed)));
            using var indent = new Indent(this);
            return base.VisitBlock(node);
        }

        public override SyntaxNode? VisitBaseList(BaseListSyntax node)
        {
            if (node.Parent is EnumDeclarationSyntax)
            {
                return base.VisitBaseList(node);
            }
            else
            {
                return base.VisitBaseList(this.WithIndentingTrivia(node));
            }
        }

        public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
        {
            if (node.Parent is ParameterSyntax)
            {
                return node.WithCloseBracketToken(TokenWithSpace(SyntaxKind.CloseBracketToken));
            }
            else if (node.Parent is BaseTypeDeclarationSyntax)
            {
                return this.WithOuterIndentingTrivia(node);
            }
            else
            {
                return this.WithIndentingTrivia(node);
            }
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) => base.VisitMethodDeclaration(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node) => base.VisitConstructorDeclaration(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node) => base.VisitOperatorDeclaration(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node) => base.VisitConversionOperatorDeclaration(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitDelegateDeclaration(DelegateDeclarationSyntax node) => base.VisitDelegateDeclaration(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node) => base.VisitFieldDeclaration(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node) => base.VisitEnumMemberDeclaration(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node) => base.VisitPropertyDeclaration(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitIndexerDeclaration(IndexerDeclarationSyntax node) => base.VisitIndexerDeclaration(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitAccessorList(AccessorListSyntax node)
        {
            node = node
                .WithOpenBraceToken(Token(TriviaList(this.IndentTrivia), SyntaxKind.OpenBraceToken, TriviaList(LineFeed)))
                .WithCloseBraceToken(Token(TriviaList(this.IndentTrivia), SyntaxKind.CloseBraceToken, TriviaList(LineFeed)));
            using var indent = new Indent(this);
            return base.VisitAccessorList(node);
        }

        public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            if (node.Body is not null)
            {
                node = node.WithKeyword(node.Keyword.WithTrailingTrivia(LineFeed));
            }

            return base.VisitAccessorDeclaration(this.WithIndentingTrivia(node));
        }

        public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node) => base.VisitLocalDeclarationStatement(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) => base.VisitExpressionStatement(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitInitializerExpression(InitializerExpressionSyntax node)
        {
            node = node
                .WithOpenBraceToken(Token(TriviaList(this.IndentTrivia), SyntaxKind.OpenBraceToken, TriviaList(LineFeed)))
                .WithCloseBraceToken(Token(TriviaList(this.IndentTrivia), SyntaxKind.CloseBraceToken, TriviaList(LineFeed)))
                .WithTrailingTrivia(TriviaList());
            using var indent = new Indent(this);
            return base.VisitInitializerExpression(node);
        }

        public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (node.Parent is InitializerExpressionSyntax)
            {
                return base.VisitAssignmentExpression(this.WithIndentingTrivia(node));
            }
            else
            {
                return base.VisitAssignmentExpression(node);
            }
        }

        public override SyntaxNode? VisitTryStatement(TryStatementSyntax node) => base.VisitTryStatement(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitThrowStatement(ThrowStatementSyntax node) => base.VisitThrowStatement(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitCatchClause(CatchClauseSyntax node) => base.VisitCatchClause(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitFinallyClause(FinallyClauseSyntax node) => base.VisitFinallyClause(this.WithIndentingTrivia(node));

        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
        {
            node = this.WithIndentingTrivia(node);
            if (node.Statement is BlockSyntax)
            {
                return base.VisitIfStatement(node);
            }
            else
            {
                using var indent = new Indent(this);
                return base.VisitIfStatement(node);
            }
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            node = this.WithIndentingTrivia(node);
            if (node.Statement is BlockSyntax)
            {
                return base.VisitWhileStatement(node);
            }
            else
            {
                using var indent = new Indent(this);
                return base.VisitWhileStatement(node);
            }
        }

        public override SyntaxNode? VisitElseClause(ElseClauseSyntax node)
        {
            node = this.WithIndentingTrivia(node);
            if (node.Statement is BlockSyntax)
            {
                return base.VisitElseClause(node);
            }
            else
            {
                using var indent = new Indent(this);
                return base.VisitElseClause(node);
            }
        }

        public override SyntaxNode? VisitFixedStatement(FixedStatementSyntax node)
        {
            node = this.WithIndentingTrivia(node);
            if (node.Statement is BlockSyntax)
            {
                return base.VisitFixedStatement(node);
            }
            else
            {
                using var indent = new Indent(this);
                return base.VisitFixedStatement(node);
            }
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            node = this.WithIndentingTrivia(node);
            if (node.Statement is BlockSyntax)
            {
                return base.VisitForStatement(node);
            }
            else
            {
                using var indent = new Indent(this);
                return base.VisitForStatement(node);
            }
        }

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            return base.VisitReturnStatement(this.WithIndentingTrivia(node));
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => base.VisitLocalFunctionStatement(this.WithIndentingTrivia(node));

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (token.IsKind(SyntaxKind.CommaToken) && token.Parent is ParameterListSyntax or AttributeArgumentListSyntax or ArgumentListSyntax)
            {
                return TokenWithSpace(SyntaxKind.CommaToken);
            }

            return base.VisitToken(token);
        }

        public override SyntaxTriviaList VisitList(SyntaxTriviaList list)
        {
#if DEBUG && false // Nodes that contain any annotations at all cause a lot of lock contention that slows us down. Consider removing it all and enforcing (part of it) with this code
            if (list.Any() && list[0].IsEquivalentTo(SyntaxFactory.ElasticMarker))
            {
                throw new GenerationFailedException("Elastic trivia got by us.");
            }
#endif

            string? indent = null;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].GetStructure() is DocumentationCommentTriviaSyntax trivia)
                {
                    indent ??= list[i].Token.Parent is BaseTypeDeclarationSyntax or AttributeListSyntax { Parent: BaseTypeDeclarationSyntax } ? this.OuterIndentTrivia.ToString() : this.IndentTrivia.ToString();
                    var comment = new StringBuilder(trivia.Content.ToFullString());
                    comment.Insert(0, indent);
                    comment.Replace("\n", "\n" + indent);
                    comment.Length -= indent.Length; // Remove the extra indent after the last newline.
                    list = list.RemoveAt(i).InsertRange(i, ParseLeadingTrivia(comment.ToString()));
                }
            }

            return list; // do not recurse into trivia
        }

        private static SyntaxList<MemberDeclarationSyntax> AddSpacingBetweenMembers(SyntaxList<MemberDeclarationSyntax> members, bool insertLineAboveFirstMember = false)
        {
            List<MemberDeclarationSyntax> mutableMembers = members.ToList();
            for (int i = mutableMembers.Count - 1; i > 0; i--)
            {
                if (mutableMembers[i] is
                    ClassDeclarationSyntax or
                    StructDeclarationSyntax or
                    NamespaceDeclarationSyntax or
                    EnumDeclarationSyntax or
                    BaseMethodDeclarationSyntax or
                    IndexerDeclarationSyntax or
                    PropertyDeclarationSyntax or
                    FieldDeclarationSyntax)
                {
                    mutableMembers[i] = mutableMembers[i].WithLeadingTrivia(mutableMembers[i].GetLeadingTrivia().Insert(0, LineFeed));
                }
            }

            if (insertLineAboveFirstMember && mutableMembers.Count > 0)
            {
                mutableMembers[0] = mutableMembers[0].WithLeadingTrivia(mutableMembers[0].GetLeadingTrivia().Insert(0, LineFeed));
            }

            return new SyntaxList<MemberDeclarationSyntax>(mutableMembers);
        }

        private TSyntax WithIndentingTrivia<TSyntax>(TSyntax node, SyntaxTrivia indentTrivia)
            where TSyntax : SyntaxNode
        {
            if (node is MemberDeclarationSyntax memberDeclaration)
            {
                SyntaxToken firstToken = GetFirstToken(memberDeclaration);
                return node.ReplaceToken(firstToken, firstToken.WithLeadingTrivia(firstToken.HasLeadingTrivia ? firstToken.LeadingTrivia.Add(indentTrivia) : TriviaList(indentTrivia)));
            }

            if (node is LocalFunctionStatementSyntax localFunction)
            {
                SyntaxToken firstToken = GetFirstTokenForFunction(localFunction);
                return node.ReplaceToken(firstToken, firstToken.WithLeadingTrivia(firstToken.HasLeadingTrivia ? firstToken.LeadingTrivia.Add(indentTrivia) : TriviaList(indentTrivia)));
            }

            // Take care to preserve xml doc comments, pragmas, etc.
            return node.WithLeadingTrivia(node.HasLeadingTrivia ? this.VisitList(node.GetLeadingTrivia()).Add(indentTrivia) : TriviaList(indentTrivia));

            static SyntaxToken GetFirstToken(MemberDeclarationSyntax memberDeclaration)
            {
                if (!memberDeclaration.AttributeLists.Any())
                {
                    return memberDeclaration.GetFirstToken();
                }
                else if (memberDeclaration.Modifiers.Any())
                {
                    return memberDeclaration.Modifiers[0];
                }
                else
                {
                    return memberDeclaration.GetFirstToken();
                }
            }

            static SyntaxToken GetFirstTokenForFunction(LocalFunctionStatementSyntax localFunction)
            {
                if (!localFunction.AttributeLists.Any())
                {
                    return localFunction.GetFirstToken();
                }
                else if (localFunction.Modifiers.Any())
                {
                    return localFunction.Modifiers[0];
                }
                else
                {
                    return localFunction.GetFirstToken();
                }
            }
        }

        private TSyntax WithIndentingTrivia<TSyntax>(TSyntax node)
            where TSyntax : SyntaxNode
        {
            return this.WithIndentingTrivia(node, this.IndentTrivia);
        }

        private TSyntax WithOuterIndentingTrivia<TSyntax>(TSyntax node)
            where TSyntax : SyntaxNode
        {
            return this.WithIndentingTrivia(node, this.OuterIndentTrivia);
        }

        private struct Indent : IDisposable
        {
            private readonly WhitespaceRewriter rewriter;

            internal Indent(WhitespaceRewriter rewriter)
            {
                this.rewriter = rewriter;
                rewriter.indentationLevel++;
                for (int i = rewriter.indentationLevels.Count; i <= rewriter.indentationLevel; i++)
                {
                    rewriter.indentationLevels.Add(SyntaxTrivia(SyntaxKind.WhitespaceTrivia, new string('\t', i)));
                }
            }

            public void Dispose()
            {
                this.rewriter.indentationLevel--;
            }
        }
    }
}
