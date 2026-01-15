// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

/// <summary>
/// A <see cref="SyntaxFactory"/> that never adds elastic annotations to tokens.
/// </summary>
/// <remarks>
/// Elastic annotations causes green-tree rewrites to enter a ConditionalWeakTable and on .NET Framework this requires a lock.
/// This lock generates a lot of contention while we are fixing whitespace with our syntax rewriter.
/// </remarks>
internal static class FastSyntaxFactory
{
    internal static readonly SyntaxToken Semicolon = Token(TriviaList(), SyntaxKind.SemicolonToken, TriviaList(LineFeed));

    internal static readonly SyntaxToken OpenBrace = Token(TriviaList(), SyntaxKind.OpenBraceToken, TriviaList(LineFeed));

    internal static readonly SyntaxToken CloseBrace = Token(TriviaList(), SyntaxKind.CloseBraceToken, TriviaList(LineFeed));

    internal static SyntaxTrivia Space => SyntaxFactory.Space;

    internal static SyntaxTrivia LineFeed => SyntaxFactory.LineFeed;

    internal static SyntaxTrivia SyntaxTrivia(SyntaxKind kind, string text) => SyntaxFactory.SyntaxTrivia(kind, text);

    internal static SyntaxTokenList TokenList() => SyntaxFactory.TokenList();

    internal static SyntaxTokenList TokenList(params SyntaxToken[] tokens) => SyntaxFactory.TokenList(tokens);

    internal static SyntaxToken Token(SyntaxKind kind)
    {
        SyntaxTriviaList trailingTrivia = kind switch
        {
            SyntaxKind.AsKeyword => TriviaList(Space),
            SyntaxKind.IsKeyword => TriviaList(Space),
            SyntaxKind.WhileKeyword => TriviaList(LineFeed),
            SyntaxKind.IfKeyword => TriviaList(LineFeed),
            SyntaxKind.SemicolonToken => TriviaList(LineFeed),
            SyntaxKind.FixedKeyword => TriviaList(Space),
            SyntaxKind.ImplicitKeyword => TriviaList(Space),
            SyntaxKind.ExplicitKeyword => TriviaList(Space),
            SyntaxKind.ExternKeyword => TriviaList(Space),
            SyntaxKind.OverrideKeyword => TriviaList(Space),
            SyntaxKind.PublicKeyword => TriviaList(Space),
            SyntaxKind.ProtectedKeyword => TriviaList(Space),
            SyntaxKind.InternalKeyword => TriviaList(Space),
            SyntaxKind.RefKeyword => TriviaList(Space),
            SyntaxKind.InKeyword => TriviaList(Space),
            SyntaxKind.OutKeyword => TriviaList(Space),
            SyntaxKind.UnsafeKeyword => TriviaList(Space),
            SyntaxKind.NewKeyword => TriviaList(Space),
            SyntaxKind.StructKeyword => TriviaList(Space),
            SyntaxKind.NamespaceKeyword => TriviaList(Space),
            SyntaxKind.ClassKeyword => TriviaList(Space),
            SyntaxKind.DelegateKeyword => TriviaList(Space),
            SyntaxKind.EnumKeyword => TriviaList(Space),
            SyntaxKind.CaseKeyword => TriviaList(Space),
            SyntaxKind.SwitchKeyword => TriviaList(Space),
            SyntaxKind.UsingKeyword => TriviaList(Space),
            SyntaxKind.StaticKeyword => TriviaList(Space),
            SyntaxKind.EqualsToken => TriviaList(Space),
            SyntaxKind.EqualsEqualsToken => TriviaList(Space),
            _ => TriviaList(),
        };
        return SyntaxFactory.Token(TriviaList(), kind, trailingTrivia);
    }

    internal static SyntaxToken Token(SyntaxTriviaList leadingTrivia, SyntaxKind kind, SyntaxTriviaList trailingTrivia) => SyntaxFactory.Token(leadingTrivia, kind, trailingTrivia);

    internal static SyntaxToken Token(SyntaxTriviaList leadingTrivia, SyntaxKind kind, string text, string valueText, SyntaxTriviaList trailingTrivia) => SyntaxFactory.Token(leadingTrivia, kind, text, valueText, trailingTrivia);

    internal static BlockSyntax Block() => SyntaxFactory.Block(OpenBrace, List<StatementSyntax>(), CloseBrace);

    internal static BlockSyntax Block(params StatementSyntax[] statements) => SyntaxFactory.Block(OpenBrace, List(statements), CloseBrace);

    internal static BlockSyntax Block(IEnumerable<StatementSyntax> statements) => SyntaxFactory.Block(OpenBrace, List(statements), CloseBrace);

    internal static ImplicitArrayCreationExpressionSyntax ImplicitArrayCreationExpression(InitializerExpressionSyntax initializerExpression) => SyntaxFactory.ImplicitArrayCreationExpression(Token(SyntaxKind.NewKeyword), Token(SyntaxKind.OpenBracketToken), default, Token(SyntaxKind.CloseBracketToken), initializerExpression);

    internal static ForStatementSyntax ForStatement(VariableDeclarationSyntax? declaration, ExpressionSyntax condition, SeparatedSyntaxList<ExpressionSyntax> incrementors, StatementSyntax statement)
    {
        SyntaxToken semicolonToken = SyntaxFactory.Token(TriviaList(), SyntaxKind.SemicolonToken, TriviaList(Space));
        return SyntaxFactory.ForStatement(Token(SyntaxKind.ForKeyword), Token(SyntaxKind.OpenParenToken), declaration!, default, semicolonToken, condition, semicolonToken, incrementors, Token(SyntaxKind.CloseParenToken), statement);
    }

    internal static ForEachStatementSyntax ForEachStatement(TypeSyntax type, SyntaxToken identifier, ExpressionSyntax expression, StatementSyntax statement) => SyntaxFactory.ForEachStatement(type, identifier, expression, statement);

    internal static StatementSyntax EmptyStatement() => SyntaxFactory.EmptyStatement(Token(SyntaxKind.SemicolonToken));

    internal static NamespaceDeclarationSyntax NamespaceDeclaration(NameSyntax name) => SyntaxFactory.NamespaceDeclaration(Token(TriviaList(), SyntaxKind.NamespaceKeyword, TriviaList(Space)), name.WithTrailingTrivia(LineFeed), OpenBrace, default, default, default, CloseBrace, default);

    internal static InterfaceDeclarationSyntax InterfaceDeclaration(SyntaxToken name) => SyntaxFactory.InterfaceDeclaration(default, default, Token(TriviaList(), SyntaxKind.InterfaceKeyword, TriviaList(Space)), name.WithTrailingTrivia(LineFeed), null, null, default, Token(TriviaList(), SyntaxKind.OpenBraceToken, TriviaList(LineFeed)), default, Token(TriviaList(), SyntaxKind.CloseBraceToken, TriviaList(LineFeed)), default);

    internal static InvocationExpressionSyntax InvocationExpression(ExpressionSyntax expression) => SyntaxFactory.InvocationExpression(expression, ArgumentList());

    internal static InvocationExpressionSyntax InvocationExpression(ExpressionSyntax expression, ArgumentListSyntax argumentList) => SyntaxFactory.InvocationExpression(expression, argumentList);

    internal static DeclarationPatternSyntax DeclarationPattern(TypeSyntax type, VariableDesignationSyntax designation) => SyntaxFactory.DeclarationPattern(type.WithTrailingTrivia(TriviaList(Space)), designation);

    internal static LocalDeclarationStatementSyntax LocalDeclarationStatement(VariableDeclarationSyntax declaration) => SyntaxFactory.LocalDeclarationStatement(TokenList(), declaration, Semicolon);

    internal static DeclarationExpressionSyntax DeclarationExpression(TypeSyntax type, VariableDesignationSyntax designation) => SyntaxFactory.DeclarationExpression(type, designation);

    internal static VariableDeclaratorSyntax VariableDeclarator(SyntaxToken identifier, EqualsValueClauseSyntax? initializer = null) => SyntaxFactory.VariableDeclarator(identifier, argumentList: null, initializer: initializer);

    internal static VariableDeclarationSyntax VariableDeclaration(TypeSyntax type) => SyntaxFactory.VariableDeclaration(type.WithTrailingTrivia(TriviaList(Space)));

    internal static VariableDeclarationSyntax VariableDeclaration(TypeSyntax type, params VariableDeclaratorSyntax[] variables) => SyntaxFactory.VariableDeclaration(type.WithTrailingTrivia(TriviaList(Space)), SeparatedList(variables));

    internal static SizeOfExpressionSyntax SizeOfExpression(TypeSyntax type) => SyntaxFactory.SizeOfExpression(Token(SyntaxKind.SizeOfKeyword), Token(SyntaxKind.OpenParenToken), type, Token(SyntaxKind.CloseParenToken));

    internal static MemberAccessExpressionSyntax MemberAccessExpression(SyntaxKind kind, ExpressionSyntax expression, SimpleNameSyntax name) => SyntaxFactory.MemberAccessExpression(kind, expression, Token(GetMemberAccessExpressionOperatorTokenKind(kind)), name);

    internal static ConditionalAccessExpressionSyntax ConditionalAccessExpression(ExpressionSyntax expression, SimpleNameSyntax name) => SyntaxFactory.ConditionalAccessExpression(expression, Token(SyntaxKind.QuestionToken), MemberBindingExpression(name));

    internal static MemberBindingExpressionSyntax MemberBindingExpression(SimpleNameSyntax name) => SyntaxFactory.MemberBindingExpression(Token(SyntaxKind.DotToken), name);

    internal static NameColonSyntax NameColon(IdentifierNameSyntax name) => SyntaxFactory.NameColon(name, Token(TriviaList(), SyntaxKind.ColonToken, TriviaList(Space)));

    internal static UsingDirectiveSyntax UsingDirective(NameSyntax name) => SyntaxFactory.UsingDirective(Token(TriviaList(), SyntaxKind.UsingKeyword, TriviaList(Space)), default, null, name, Semicolon);

    internal static UsingDirectiveSyntax UsingDirective(NameEqualsSyntax alias, NameSyntax name) => SyntaxFactory.UsingDirective(Token(TriviaList(), SyntaxKind.UsingKeyword, TriviaList(Space)), default, alias, name, Semicolon);

    internal static AliasQualifiedNameSyntax AliasQualifiedName(IdentifierNameSyntax alias, SimpleNameSyntax name) => SyntaxFactory.AliasQualifiedName(alias, Token(SyntaxKind.ColonColonToken), name);

    internal static WhileStatementSyntax WhileStatement(ExpressionSyntax expression, StatementSyntax statement) => SyntaxFactory.WhileStatement(Token(TriviaList(), SyntaxKind.WhileKeyword, TriviaList(Space)), Token(SyntaxKind.OpenParenToken), expression, Token(TriviaList(), SyntaxKind.CloseParenToken, TriviaList(LineFeed)), statement);

    internal static TryStatementSyntax TryStatement(BlockSyntax block, SyntaxList<CatchClauseSyntax> catches, FinallyClauseSyntax? @finally) => SyntaxFactory.TryStatement(Token(TriviaList(), SyntaxKind.TryKeyword, TriviaList(LineFeed)), block, catches, @finally!);

    internal static CatchClauseSyntax CatchClause(CatchDeclarationSyntax? catchDeclaration, CatchFilterClauseSyntax? filter, BlockSyntax block) => SyntaxFactory.CatchClause(TokenWithSpace(SyntaxKind.CatchKeyword), catchDeclaration, filter, block);

    internal static CatchDeclarationSyntax CatchDeclaration(TypeSyntax type, SyntaxToken identifier) => SyntaxFactory.CatchDeclaration(Token(SyntaxKind.OpenParenToken), type, identifier, Token(SyntaxKind.CloseParenToken));

    internal static SwitchSectionSyntax SwitchSection() => SyntaxFactory.SwitchSection();

    internal static SwitchStatementSyntax SwitchStatement(ExpressionSyntax expression) => SyntaxFactory.SwitchStatement(TokenWithSpace(SyntaxKind.SwitchKeyword), Token(SyntaxKind.OpenParenToken), expression, TokenWithLineFeed(SyntaxKind.CloseParenToken), OpenBrace, default, CloseBrace);

    internal static DefaultSwitchLabelSyntax DefaultSwitchLabel() => SyntaxFactory.DefaultSwitchLabel(Token(SyntaxKind.DefaultKeyword), Token(SyntaxKind.ColonToken));

    internal static CaseSwitchLabelSyntax CaseSwitchLabel(ExpressionSyntax value) => SyntaxFactory.CaseSwitchLabel(TokenWithSpace(SyntaxKind.CaseKeyword), value, TokenWithSpace(SyntaxKind.ColonToken));

    internal static ArrowExpressionClauseSyntax ArrowExpressionClause(ExpressionSyntax expression) => SyntaxFactory.ArrowExpressionClause(TokenWithSpaces(SyntaxKind.EqualsGreaterThanToken), expression);

    internal static BracketedArgumentListSyntax BracketedArgumentList(SeparatedSyntaxList<ArgumentSyntax> arguments = default) => SyntaxFactory.BracketedArgumentList(Token(SyntaxKind.OpenBracketToken), arguments, Token(SyntaxKind.CloseBracketToken));

    internal static AttributeTargetSpecifierSyntax AttributeTargetSpecifier(SyntaxToken identifier) => SyntaxFactory.AttributeTargetSpecifier(identifier, TokenWithSpace(SyntaxKind.ColonToken));

    internal static ThrowStatementSyntax ThrowStatement() => SyntaxFactory.ThrowStatement(default, Token(SyntaxKind.ThrowKeyword), null, Semicolon);

    internal static ThrowStatementSyntax ThrowStatement(ExpressionSyntax expression) => SyntaxFactory.ThrowStatement(Token(TriviaList(), SyntaxKind.ThrowKeyword, TriviaList(Space)), expression, Semicolon);

    internal static ThrowExpressionSyntax ThrowExpression(ExpressionSyntax expression) => SyntaxFactory.ThrowExpression(Token(TriviaList(), SyntaxKind.ThrowKeyword, TriviaList(Space)), expression);

    internal static ExpressionSyntax NameOfExpression(IdentifierNameSyntax identifierName) => SyntaxFactory.InvocationExpression(IdentifierName("nameof"), ArgumentList(SingletonSeparatedList(Argument(identifierName))));

    internal static ReturnStatementSyntax ReturnStatement(ExpressionSyntax? expression) => SyntaxFactory.ReturnStatement(Token(TriviaList(), SyntaxKind.ReturnKeyword, TriviaList(Space)), expression!, Semicolon);

    internal static DelegateDeclarationSyntax DelegateDeclaration(TypeSyntax returnType, SyntaxToken identifier) => SyntaxFactory.DelegateDeclaration(default(SyntaxList<AttributeListSyntax>), default(SyntaxTokenList), Token(TriviaList(), SyntaxKind.DelegateKeyword, TriviaList(Space)), returnType.WithTrailingTrivia(TriviaList(Space)), identifier, null, ParameterList(), default, Semicolon);

    internal static OperatorDeclarationSyntax OperatorDeclaration(TypeSyntax returnType, SyntaxToken operatorToken) => SyntaxFactory.OperatorDeclaration(default(SyntaxList<AttributeListSyntax>), default(SyntaxTokenList), returnType.WithTrailingTrivia(TriviaList(Space)), Token(SyntaxKind.OperatorKeyword), operatorToken, ParameterList(), null, null, default(SyntaxToken));

    internal static ConversionOperatorDeclarationSyntax ConversionOperatorDeclaration(SyntaxToken implicitOrExplicitKeyword, TypeSyntax type) => SyntaxFactory.ConversionOperatorDeclaration(default, default, implicitOrExplicitKeyword, TokenWithSpace(SyntaxKind.OperatorKeyword), type, ParameterList(), null, null, default);

    internal static ConstructorDeclarationSyntax ConstructorDeclaration(SyntaxToken identifier) => SyntaxFactory.ConstructorDeclaration(default, default, identifier, ParameterList(), null, null, null, default);

    internal static ClassDeclarationSyntax ClassDeclaration(SyntaxToken identifier) => SyntaxFactory.ClassDeclaration(default, default, Token(SyntaxKind.ClassKeyword), identifier.WithTrailingTrivia(TriviaList(LineFeed)), null, null, default, OpenBrace, default, CloseBrace, default);

    internal static StructDeclarationSyntax StructDeclaration(SyntaxToken identifier) => SyntaxFactory.StructDeclaration(default, default, TokenWithSpace(SyntaxKind.StructKeyword), identifier.WithTrailingTrivia(TriviaList(LineFeed)), null, null, default, OpenBrace, default, CloseBrace, default);

    internal static ConstructorInitializerSyntax ConstructorInitializer(SyntaxKind kind) => SyntaxFactory.ConstructorInitializer(kind, Token(SyntaxKind.ColonToken), Token(GetConstructorInitializerThisOrBaseKeywordKind(kind)), ArgumentList());

    internal static ConstructorInitializerSyntax ConstructorInitializer(SyntaxKind kind, ArgumentListSyntax argumentList) => SyntaxFactory.ConstructorInitializer(kind, Token(SyntaxKind.ColonToken), Token(GetConstructorInitializerThisOrBaseKeywordKind(kind)), argumentList);

    internal static PropertyDeclarationSyntax PropertyDeclaration(TypeSyntax type, string identifier) => PropertyDeclaration(type, Identifier(identifier));

    internal static PropertyDeclarationSyntax PropertyDeclaration(TypeSyntax type, SyntaxToken identifier) => SyntaxFactory.PropertyDeclaration(type, identifier);

    internal static AccessorDeclarationSyntax AccessorDeclaration(SyntaxKind kind) => AccessorDeclaration(kind, null);

    internal static AccessorDeclarationSyntax AccessorDeclaration(SyntaxKind kind, BlockSyntax? body) => SyntaxFactory.AccessorDeclaration(kind, default, default, Token(GetAccessorDeclarationKeywordKind(kind)), body, null, default);

    internal static AccessorListSyntax AccessorList() => SyntaxFactory.AccessorList(OpenBrace, default, CloseBrace);

    internal static IndexerDeclarationSyntax IndexerDeclaration(TypeSyntax type) => SyntaxFactory.IndexerDeclaration(default, default, type, null, Token(SyntaxKind.ThisKeyword), BracketedParameterList(), null, null, default);

    internal static ElementAccessExpressionSyntax ElementAccessExpression(ExpressionSyntax expression) => SyntaxFactory.ElementAccessExpression(expression, BracketedArgumentList());

    internal static EnumDeclarationSyntax EnumDeclaration(SyntaxToken identifier) => SyntaxFactory.EnumDeclaration(default, default, Token(TriviaList(), SyntaxKind.EnumKeyword, TriviaList(Space)), identifier.WithTrailingTrivia(LineFeed), null, Token(TriviaList(), SyntaxKind.OpenBraceToken, TriviaList(LineFeed)), default, Token(TriviaList(), SyntaxKind.CloseBraceToken, TriviaList(LineFeed)), default);

    internal static EnumMemberDeclarationSyntax EnumMemberDeclaration(SyntaxToken identifier) => SyntaxFactory.EnumMemberDeclaration(identifier);

    internal static BracketedParameterListSyntax BracketedParameterList() => SyntaxFactory.BracketedParameterList(Token(SyntaxKind.OpenBracketToken), default, Token(SyntaxKind.CloseBracketToken));

    internal static InitializerExpressionSyntax InitializerExpression(SyntaxKind kind, SeparatedSyntaxList<ExpressionSyntax> expressions) => SyntaxFactory.InitializerExpression(kind, OpenBrace, expressions, CloseBrace);

    internal static ObjectCreationExpressionSyntax ObjectCreationExpression(TypeSyntax type, SeparatedSyntaxList<ArgumentSyntax> arguments = default) => SyntaxFactory.ObjectCreationExpression(Token(TriviaList(), SyntaxKind.NewKeyword, TriviaList(Space)), type, ArgumentList(arguments), null);

    internal static ArrayCreationExpressionSyntax ArrayCreationExpression(ArrayTypeSyntax type, InitializerExpressionSyntax? initializer = null) => SyntaxFactory.ArrayCreationExpression(Token(SyntaxKind.NewKeyword), type, initializer);

    internal static XmlCrefAttributeSyntax XmlCrefAttribute(CrefSyntax cref) => XmlCrefAttribute(cref, SyntaxKind.DoubleQuoteToken);

    internal static XmlCrefAttributeSyntax XmlCrefAttribute(CrefSyntax cref, SyntaxKind quoteKind)
    {
        cref = cref.ReplaceTokens(cref.DescendantTokens(), XmlReplaceBracketTokens);
        return SyntaxFactory.XmlCrefAttribute(XmlName("cref"), TokenWithNoSpace(SyntaxKind.EqualsToken), Token(quoteKind), cref, Token(quoteKind)).WithLeadingTrivia(TriviaList(Space));
    }

    internal static CrefParameterSyntax CrefParameter(TypeSyntax type) => SyntaxFactory.CrefParameter(default, type);

    internal static CrefParameterSyntax CrefParameter(SyntaxToken refKindKeyword, TypeSyntax type) => SyntaxFactory.CrefParameter(refKindKeyword, type);

    internal static CrefParameterListSyntax CrefParameterList(SeparatedSyntaxList<CrefParameterSyntax> parameters = default) => SyntaxFactory.CrefParameterList(Token(SyntaxKind.OpenParenToken), parameters, Token(SyntaxKind.CloseParenToken));

    internal static NameMemberCrefSyntax NameMemberCref(TypeSyntax name, CrefParameterListSyntax parameters) => SyntaxFactory.NameMemberCref(name, parameters);

    internal static XmlElementSyntax XmlElement(string localName, SyntaxList<XmlNodeSyntax> content) => SyntaxFactory.XmlElement(XmlName(localName), content);

    internal static XmlNameSyntax XmlName(string text) => SyntaxFactory.XmlName(Identifier(text));

    internal static XmlTextSyntax XmlText(string text) => SyntaxFactory.XmlText(text);

    internal static XmlEmptyElementSyntax XmlEmptyElement(string name) => SyntaxFactory.XmlEmptyElement(Token(SyntaxKind.LessThanToken), XmlName(name), default, Token(SyntaxKind.SlashGreaterThanToken));

    internal static XmlTextSyntax XmlText(params SyntaxToken[] textTokens) => SyntaxFactory.XmlText(textTokens);

    internal static DocumentationCommentTriviaSyntax DocumentationCommentTrivia(SyntaxKind kind, SyntaxList<XmlNodeSyntax> content = default) => SyntaxFactory.DocumentationCommentTrivia(kind, content, Token(SyntaxKind.EndOfDocumentationCommentToken));

    internal static SyntaxTrivia DocumentationCommentExterior(string text) => SyntaxFactory.DocumentationCommentExterior(text);

    internal static PragmaWarningDirectiveTriviaSyntax PragmaWarningDirectiveTrivia(SyntaxToken disableOrRestoreKeyword, SeparatedSyntaxList<ExpressionSyntax> errorCodes, bool isActive) => SyntaxFactory.PragmaWarningDirectiveTrivia(
        hashToken: Token(SyntaxKind.HashToken),
        pragmaKeyword: TokenWithSpace(SyntaxKind.PragmaKeyword),
        warningKeyword: TokenWithSpace(SyntaxKind.WarningKeyword),
        disableOrRestoreKeyword,
        errorCodes,
        endOfDirectiveToken: TokenWithLineFeed(SyntaxKind.EndOfDirectiveToken),
        isActive);

    internal static SeparatedSyntaxList<TNode> SeparatedList<TNode>(IEnumerable<TNode>? nodes)
        where TNode : SyntaxNode => SyntaxFactory.SeparatedList(nodes);

    internal static SeparatedSyntaxList<TNode> SeparatedList<TNode>(IEnumerable<SyntaxNodeOrToken> nodesOrTokens)
        where TNode : SyntaxNode => SyntaxFactory.SeparatedList<TNode>(nodesOrTokens);

    internal static ParameterListSyntax FixTrivia(ParameterListSyntax parameterList) => parameterList.WithParameters(FixTrivia(parameterList.Parameters));

    internal static ArgumentListSyntax FixTrivia(ArgumentListSyntax argumentList) => argumentList.WithArguments(FixTrivia(argumentList.Arguments));

    internal static AttributeArgumentListSyntax FixTrivia(AttributeArgumentListSyntax argumentList) => argumentList.WithArguments(FixTrivia(argumentList.Arguments));

    internal static SeparatedSyntaxList<TNode> FixTrivia<TNode>(SeparatedSyntaxList<TNode> list)
        where TNode : SyntaxNode
    {
        for (int i = 0; i < list.SeparatorCount; i++)
        {
            SyntaxToken separator = list.GetSeparator(i);
            list = list.ReplaceSeparator(separator, TokenWithSpace(separator.Kind()));
        }

        return list;
    }

    internal static SyntaxToken XmlTextNewLine(string text) => XmlTextNewLine(text, true);

    internal static SyntaxToken XmlTextNewLine(string text, bool continueXmlDocumentationComment)
    {
        SyntaxToken token = SyntaxFactory.XmlTextNewLine(TriviaList(), text, text, TriviaList());
        if (continueXmlDocumentationComment)
        {
            token = token.WithTrailingTrivia(token.TrailingTrivia.Add(DocumentationCommentExterior("/// ")));
            return token;
        }

        return token;
    }

    internal static MethodDeclarationSyntax MethodDeclaration(TypeSyntax returnType, SyntaxToken identifier) => SyntaxFactory.MethodDeclaration(default(SyntaxList<AttributeListSyntax>), default(SyntaxTokenList), returnType.WithTrailingTrivia(TriviaList(Space)), null, identifier, null, ParameterList(), default(SyntaxList<TypeParameterConstraintClauseSyntax>), null, null, default(SyntaxToken));

    internal static LocalFunctionStatementSyntax LocalFunctionStatement(TypeSyntax returnType, SyntaxToken identifier) => SyntaxFactory.LocalFunctionStatement(default(SyntaxList<AttributeListSyntax>), default(SyntaxTokenList), returnType, identifier, null, ParameterList(), default(SyntaxList<TypeParameterConstraintClauseSyntax>), null, null);

    internal static MethodDeclarationSyntax MethodDeclaration(SyntaxList<AttributeListSyntax> attributeLists, SyntaxTokenList modifiers, TypeSyntax returnType, ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier, SyntaxToken identifier, TypeParameterListSyntax? typeParameterList, ParameterListSyntax parameterList, SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses, BlockSyntax body, SyntaxToken semicolonToken) => SyntaxFactory.MethodDeclaration(attributeLists, modifiers, returnType.WithTrailingTrivia(TriviaList(Space)), explicitInterfaceSpecifier!, identifier, typeParameterList!, parameterList, constraintClauses, body, semicolonToken);

    internal static MemberDeclarationSyntax? ParseMemberDeclaration(string text, ParseOptions? options) => SyntaxFactory.ParseMemberDeclaration(text, options: options);

    internal static SingleVariableDesignationSyntax SingleVariableDesignation(SyntaxToken identifier) => SyntaxFactory.SingleVariableDesignation(identifier);

    internal static SeparatedSyntaxList<TNode> SingletonSeparatedList<TNode>(TNode node)
        where TNode : SyntaxNode => SyntaxFactory.SingletonSeparatedList(node);

    internal static SyntaxList<TNode> SingletonList<TNode>(TNode node)
        where TNode : SyntaxNode => SyntaxFactory.SingletonList(node);

    internal static SyntaxTriviaList TriviaList() => SyntaxFactory.TriviaList();

    internal static SyntaxTriviaList TriviaList(SyntaxTrivia trivia) => SyntaxFactory.TriviaList(trivia);

    internal static AttributeSyntax Attribute(NameSyntax name) => SyntaxFactory.Attribute(name, AttributeArgumentList());

    internal static AttributeArgumentListSyntax AttributeArgumentList(SeparatedSyntaxList<AttributeArgumentSyntax> arguments = default) => SyntaxFactory.AttributeArgumentList(Token(SyntaxKind.OpenParenToken), arguments, Token(SyntaxKind.CloseParenToken));

    internal static AttributeListSyntax AttributeList(params SeparatedSyntaxList<AttributeSyntax> attributes) => SyntaxFactory.AttributeList(Token(SyntaxKind.OpenBracketToken), null, attributes, TokenWithLineFeed(SyntaxKind.CloseBracketToken));

    internal static SyntaxList<TNode> List<TNode>()
        where TNode : SyntaxNode => SyntaxFactory.List<TNode>();

    internal static SyntaxList<TNode> List<TNode>(IEnumerable<TNode> nodes)
        where TNode : SyntaxNode => SyntaxFactory.List(nodes);

    internal static ParameterListSyntax ParameterList() => SyntaxFactory.ParameterList(Token(SyntaxKind.OpenParenToken), SeparatedList<ParameterSyntax>(), Token(SyntaxKind.CloseParenToken));

    internal static ArgumentListSyntax ArgumentList(params SeparatedSyntaxList<ArgumentSyntax> arguments) => SyntaxFactory.ArgumentList(Token(SyntaxKind.OpenParenToken), arguments, Token(SyntaxKind.CloseParenToken));

    internal static AssignmentExpressionSyntax AssignmentExpression(SyntaxKind kind, ExpressionSyntax left, ExpressionSyntax right) => SyntaxFactory.AssignmentExpression(kind, left, Token(GetAssignmentExpressionOperatorTokenKind(kind)).WithLeadingTrivia(Space), right);

    internal static ArgumentSyntax Argument(ExpressionSyntax expression) => SyntaxFactory.Argument(expression);

    internal static ArgumentSyntax Argument(NameColonSyntax? nameColon, SyntaxToken refKindKeyword, ExpressionSyntax expression) => SyntaxFactory.Argument(nameColon, refKindKeyword, expression);

    internal static ParameterSyntax Parameter(SyntaxToken identifier) => SyntaxFactory.Parameter(identifier);

    internal static ParameterSyntax Parameter(SyntaxList<AttributeListSyntax> attributeLists, SyntaxTokenList modifiers, TypeSyntax? type, SyntaxToken identifier, EqualsValueClauseSyntax? @default) => SyntaxFactory.Parameter(attributeLists, modifiers, type, identifier, @default);

    internal static TypeParameterSyntax TypeParameter(SyntaxToken identifier) => SyntaxFactory.TypeParameter(identifier);

    internal static TypeConstraintSyntax TypeConstraint(TypeSyntax type) => SyntaxFactory.TypeConstraint(type);

    internal static ClassOrStructConstraintSyntax ClassOrStructConstraint(SyntaxKind kind) => SyntaxFactory.ClassOrStructConstraint(kind);

    internal static TypeParameterConstraintClauseSyntax TypeParameterConstraintClause(IdentifierNameSyntax name, SeparatedSyntaxList<TypeParameterConstraintSyntax> constraints) => SyntaxFactory.TypeParameterConstraintClause(TokenWithSpace(SyntaxKind.WhereKeyword), name, TokenWithSpaces(SyntaxKind.ColonToken), constraints);

    internal static FieldDeclarationSyntax FieldDeclaration(VariableDeclarationSyntax declaration) => SyntaxFactory.FieldDeclaration(default, default, declaration, Semicolon);

    internal static FunctionPointerTypeSyntax FunctionPointerType() => SyntaxFactory.FunctionPointerType(Token(SyntaxKind.DelegateKeyword), Token(SyntaxKind.AsteriskToken), null, FunctionPointerParameterList());

    internal static FunctionPointerTypeSyntax FunctionPointerType(FunctionPointerCallingConventionSyntax? callingConvention, FunctionPointerParameterListSyntax parameterList) => SyntaxFactory.FunctionPointerType(Token(SyntaxKind.DelegateKeyword), Token(SyntaxKind.AsteriskToken), callingConvention, parameterList);

    internal static FunctionPointerCallingConventionSyntax FunctionPointerCallingConvention(SyntaxToken managedOrUnmanagedKeyword) => SyntaxFactory.FunctionPointerCallingConvention(managedOrUnmanagedKeyword);

    internal static FunctionPointerCallingConventionSyntax FunctionPointerCallingConvention(SyntaxToken managedOrUnmanagedKeyword, FunctionPointerUnmanagedCallingConventionListSyntax? unmanagedCallingConventionList) => SyntaxFactory.FunctionPointerCallingConvention(managedOrUnmanagedKeyword, unmanagedCallingConventionList);

    internal static FunctionPointerUnmanagedCallingConventionSyntax FunctionPointerUnmanagedCallingConvention(SyntaxToken name) => SyntaxFactory.FunctionPointerUnmanagedCallingConvention(name);

    internal static FunctionPointerUnmanagedCallingConventionListSyntax FunctionPointerUnmanagedCallingConventionList() => SyntaxFactory.FunctionPointerUnmanagedCallingConventionList(Token(SyntaxKind.OpenBracketToken), default, Token(SyntaxKind.CloseBracketToken));

    internal static FunctionPointerUnmanagedCallingConventionListSyntax FunctionPointerUnmanagedCallingConventionList(SeparatedSyntaxList<FunctionPointerUnmanagedCallingConventionSyntax> callingConventions) => SyntaxFactory.FunctionPointerUnmanagedCallingConventionList(Token(SyntaxKind.OpenBracketToken), callingConventions, Token(SyntaxKind.CloseBracketToken));

    internal static CompilationUnitSyntax CompilationUnit() => SyntaxFactory.CompilationUnit(default, default, default, default, Token(SyntaxKind.EndOfFileToken));

    internal static FunctionPointerParameterSyntax FunctionPointerParameter(TypeSyntax type) => SyntaxFactory.FunctionPointerParameter(type);

    internal static FunctionPointerParameterListSyntax FunctionPointerParameterList() => SyntaxFactory.FunctionPointerParameterList(Token(SyntaxKind.LessThanToken), SeparatedList<FunctionPointerParameterSyntax>(), Token(SyntaxKind.GreaterThanToken));

    internal static SeparatedSyntaxList<TNode> SeparatedList<TNode>()
        where TNode : SyntaxNode => SyntaxFactory.SeparatedList<TNode>();

    internal static PredefinedTypeSyntax PredefinedType(SyntaxToken identifier) => SyntaxFactory.PredefinedType(identifier);

    internal static TypeSyntax ParseTypeName(string text) => SyntaxFactory.ParseTypeName(text);

    internal static EqualsValueClauseSyntax EqualsValueClause(ExpressionSyntax expression) => SyntaxFactory.EqualsValueClause(TokenWithSpaces(SyntaxKind.EqualsToken), expression);

    internal static NameEqualsSyntax NameEquals(string name) => NameEquals(IdentifierName(name));

    internal static NameEqualsSyntax NameEquals(IdentifierNameSyntax name) => SyntaxFactory.NameEquals(name, TokenWithSpaces(SyntaxKind.EqualsToken));

    internal static NameSyntax ParseName(string text) => SyntaxFactory.ParseName(text);

    internal static PointerTypeSyntax PointerType(TypeSyntax elementType) => SyntaxFactory.PointerType(elementType, Token(SyntaxKind.AsteriskToken));

    internal static NullableTypeSyntax NullableType(TypeSyntax elementType) => SyntaxFactory.NullableType(elementType, Token(SyntaxKind.QuestionToken));

    internal static ArrayTypeSyntax ArrayType(TypeSyntax elementType, SyntaxList<ArrayRankSpecifierSyntax> rankSpecifiers = default) => SyntaxFactory.ArrayType(elementType, rankSpecifiers);

    internal static AttributeArgumentSyntax AttributeArgument(ExpressionSyntax expression) => SyntaxFactory.AttributeArgument(null, null, expression);

    internal static CastExpressionSyntax CastExpression(TypeSyntax type, ExpressionSyntax expression) => SyntaxFactory.CastExpression(Token(SyntaxKind.OpenParenToken), type, Token(SyntaxKind.CloseParenToken), expression);

    internal static ParenthesizedExpressionSyntax ParenthesizedExpression(ExpressionSyntax expression) => SyntaxFactory.ParenthesizedExpression(Token(SyntaxKind.OpenParenToken), expression, Token(SyntaxKind.CloseParenToken));

    internal static SyntaxToken Identifier(string text) => SyntaxFactory.Identifier(TriviaList(), text, TriviaList());

    internal static SyntaxToken Identifier(SyntaxTriviaList leading, string text, SyntaxTriviaList trailing) => SyntaxFactory.Identifier(leading, text, trailing);

    internal static GenericNameSyntax GenericName(string text) => GenericName(text, TypeArgumentList());

    internal static GenericNameSyntax GenericName(string text, TypeArgumentListSyntax typeArgumentList) => SyntaxFactory.GenericName(Identifier(text), typeArgumentList);

    internal static TypeArgumentListSyntax TypeArgumentList(SeparatedSyntaxList<TypeSyntax> types = default) => SyntaxFactory.TypeArgumentList(Token(SyntaxKind.LessThanToken), types, Token(SyntaxKind.GreaterThanToken));

    internal static NameSyntax QualifiedName(NameSyntax left, SimpleNameSyntax right) => SyntaxFactory.QualifiedName(left, Token(SyntaxKind.DotToken), right);

    internal static PrefixUnaryExpressionSyntax PrefixUnaryExpression(SyntaxKind kind, ExpressionSyntax operand) => SyntaxFactory.PrefixUnaryExpression(kind, Token(GetPrefixUnaryExpressionOperatorTokenKind(kind)), operand);

    internal static PostfixUnaryExpressionSyntax PostfixUnaryExpression(SyntaxKind kind, ExpressionSyntax operand) => SyntaxFactory.PostfixUnaryExpression(kind, operand, Token(GetPostfixUnaryExpressionOperatorTokenKind(kind)));

    internal static BinaryExpressionSyntax BinaryExpression(SyntaxKind kind, ExpressionSyntax left, ExpressionSyntax right) => SyntaxFactory.BinaryExpression(kind, left, TokenWithSpaces(GetBinaryExpressionOperatorTokenKind(kind)), right);

    internal static ConstantPatternSyntax ConstantPattern(ExpressionSyntax expression) => SyntaxFactory.ConstantPattern(expression);

    internal static IsPatternExpressionSyntax IsPatternExpression(ExpressionSyntax expression, PatternSyntax pattern) => SyntaxFactory.IsPatternExpression(expression, Token(TriviaList(Space), SyntaxKind.IsKeyword, TriviaList(Space)), pattern);

    internal static BinaryPatternSyntax BinaryPattern(SyntaxKind kind, PatternSyntax left, PatternSyntax right) => SyntaxFactory.BinaryPattern(kind, left, TokenWithSpaces(GetBinaryPatternOperatorTokenKind(kind)), right);

    internal static RelationalPatternSyntax RelationalPattern(SyntaxToken operatorToken, ExpressionSyntax expression) => SyntaxFactory.RelationalPattern(operatorToken, expression);

    internal static ConditionalExpressionSyntax ConditionalExpression(ExpressionSyntax condition, ExpressionSyntax whenTrue, ExpressionSyntax whenFalse) => SyntaxFactory.ConditionalExpression(condition, Token(TriviaList(Space), SyntaxKind.QuestionToken, TriviaList(Space)), whenTrue, Token(TriviaList(Space), SyntaxKind.ColonToken, TriviaList(Space)), whenFalse);

    internal static IfStatementSyntax IfStatement(ExpressionSyntax condition, StatementSyntax whenTrue) => IfStatement(condition, whenTrue, null);

    internal static IfStatementSyntax IfStatement(ExpressionSyntax condition, StatementSyntax whenTrue, ElseClauseSyntax? whenFalse) => SyntaxFactory.IfStatement(Token(TriviaList(), SyntaxKind.IfKeyword, TriviaList(Space)), Token(SyntaxKind.OpenParenToken), condition, Token(SyntaxKind.CloseParenToken), whenTrue, whenFalse!);

    internal static ElseClauseSyntax ElseClause(StatementSyntax statement) => SyntaxFactory.ElseClause(Token(TriviaList(), SyntaxKind.ElseKeyword, TriviaList(LineFeed)), statement);

    internal static FinallyClauseSyntax FinallyClause(BlockSyntax block) => SyntaxFactory.FinallyClause(Token(TriviaList(), SyntaxKind.FinallyKeyword, TriviaList(LineFeed)), block);

    internal static ExpressionStatementSyntax ExpressionStatement(ExpressionSyntax expression) => SyntaxFactory.ExpressionStatement(expression, Semicolon);

    internal static RefExpressionSyntax RefExpression(ExpressionSyntax expression) => SyntaxFactory.RefExpression(Token(TriviaList(), SyntaxKind.RefKeyword, TriviaList(Space)), expression);

    internal static RefTypeSyntax RefType(TypeSyntax type) => SyntaxFactory.RefType(Token(TriviaList(), SyntaxKind.RefKeyword, TriviaList(Space)), type);

    internal static SimpleBaseTypeSyntax SimpleBaseType(TypeSyntax type) => SyntaxFactory.SimpleBaseType(type);

    internal static BaseListSyntax BaseList(SeparatedSyntaxList<BaseTypeSyntax> types) => SyntaxFactory.BaseList(Token(SyntaxKind.ColonToken), types);

    internal static ArrayRankSpecifierSyntax ArrayRankSpecifier() => SyntaxFactory.ArrayRankSpecifier(Token(SyntaxKind.OpenBracketToken), default, Token(SyntaxKind.CloseBracketToken));

    internal static SyntaxTrivia Trivia(StructuredTriviaSyntax node) => SyntaxFactory.Trivia(node);

    internal static CheckedExpressionSyntax CheckedExpression(ExpressionSyntax expression) => SyntaxFactory.CheckedExpression(SyntaxKind.CheckedExpression, Token(SyntaxKind.CheckedKeyword), Token(SyntaxKind.OpenParenToken), expression, Token(SyntaxKind.CloseParenToken));

    internal static CheckedExpressionSyntax UncheckedExpression(ExpressionSyntax expression) => SyntaxFactory.CheckedExpression(SyntaxKind.CheckedExpression, Token(SyntaxKind.UncheckedKeyword), Token(SyntaxKind.OpenParenToken), expression, Token(SyntaxKind.CloseParenToken));

    internal static FixedStatementSyntax FixedStatement(VariableDeclarationSyntax declaration, StatementSyntax statement) => SyntaxFactory.FixedStatement(TokenWithSpace(SyntaxKind.FixedKeyword), Token(SyntaxKind.OpenParenToken), declaration, TokenWithLineFeed(SyntaxKind.CloseParenToken), statement);

    internal static ExplicitInterfaceSpecifierSyntax ExplicitInterfaceSpecifier(NameSyntax name) => SyntaxFactory.ExplicitInterfaceSpecifier(name, Token(SyntaxKind.DotToken));

    internal static ThisExpressionSyntax ThisExpression() => SyntaxFactory.ThisExpression(Token(SyntaxKind.ThisKeyword));

    internal static DefaultExpressionSyntax DefaultExpression(TypeSyntax type) => SyntaxFactory.DefaultExpression(Token(SyntaxKind.DefaultKeyword), Token(SyntaxKind.OpenParenToken), type, Token(SyntaxKind.CloseParenToken));

    internal static LiteralExpressionSyntax LiteralExpression(SyntaxKind kind) => SyntaxFactory.LiteralExpression(kind, Token(GetLiteralExpressionTokenKind(kind)));

    internal static LiteralExpressionSyntax LiteralExpression(SyntaxKind kind, SyntaxToken token) => SyntaxFactory.LiteralExpression(kind, token);

    internal static SyntaxToken Literal(int value) => Literal(value.ToString(CultureInfo.InvariantCulture), value);

    internal static SyntaxToken Literal(string valueText, int value) => SyntaxFactory.Literal(TriviaList(), valueText, value, TriviaList());

    internal static SyntaxToken Literal(uint value) => SyntaxFactory.Literal(TriviaList(), value.ToString(CultureInfo.InvariantCulture) + "U", value, TriviaList());

    internal static SyntaxToken Literal(long value) => Literal(value.ToString(CultureInfo.InvariantCulture) + "L", value);

    internal static SyntaxToken Literal(string valueText, long value) => SyntaxFactory.Literal(TriviaList(), valueText, value, TriviaList());

    internal static SyntaxToken Literal(ulong value) => Literal(value.ToString(CultureInfo.InvariantCulture) + "UL", value);

    internal static SyntaxToken Literal(string valueText, ulong value) => SyntaxFactory.Literal(TriviaList(), valueText, value, TriviaList());

    internal static SyntaxToken Literal(double value) => SyntaxFactory.Literal(TriviaList(), value.ToString(CultureInfo.InvariantCulture), value, TriviaList());

    internal static SyntaxToken Literal(float value) => SyntaxFactory.Literal(TriviaList(), value.ToString("R", CultureInfo.InvariantCulture) + "F", value, TriviaList());

    internal static SyntaxToken Literal(string value) => SyntaxFactory.Literal(TriviaList(), SymbolDisplay.FormatLiteral(value, quote: true), value, TriviaList());

    internal static SyntaxToken Literal(char value) => SyntaxFactory.Literal(TriviaList(), SymbolDisplay.FormatLiteral(value, quote: true), value, TriviaList());

    internal static SyntaxTriviaList ParseLeadingTrivia(string text) => SyntaxFactory.ParseLeadingTrivia(text.Replace("\r\n", "\n"));

    internal static IdentifierNameSyntax IdentifierName(string name) => SyntaxFactory.IdentifierName(Identifier(name));

    internal static IdentifierNameSyntax IdentifierName(SyntaxToken identifier) => SyntaxFactory.IdentifierName(identifier);

    internal static ExpressionSyntax TypeOfExpression(TypeSyntax type) => SyntaxFactory.TypeOfExpression(Token(SyntaxKind.TypeOfKeyword), Token(SyntaxKind.OpenParenToken), type, Token(SyntaxKind.CloseParenToken));

    internal static SyntaxToken TokenWithNoSpace(SyntaxKind kind) => Token(TriviaList(), kind, TriviaList());

    internal static SyntaxToken TokenWithSpace(SyntaxKind kind) => Token(TriviaList(), kind, TriviaList(Space));

    internal static SyntaxToken TokenWithSpaces(SyntaxKind kind) => Token(TriviaList(Space), kind, TriviaList(Space));

    internal static SyntaxToken TokenWithLineFeed(SyntaxKind kind) => Token(TriviaList(), kind, TriviaList(LineFeed));

    private static SyntaxKind GetBinaryExpressionOperatorTokenKind(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.AddExpression => SyntaxKind.PlusToken,
            SyntaxKind.SubtractExpression => SyntaxKind.MinusToken,
            SyntaxKind.MultiplyExpression => SyntaxKind.AsteriskToken,
            SyntaxKind.DivideExpression => SyntaxKind.SlashToken,
            SyntaxKind.ModuloExpression => SyntaxKind.PercentToken,
            SyntaxKind.LeftShiftExpression => SyntaxKind.LessThanLessThanToken,
            SyntaxKind.RightShiftExpression => SyntaxKind.GreaterThanGreaterThanToken,
            SyntaxKind.LogicalOrExpression => SyntaxKind.BarBarToken,
            SyntaxKind.LogicalAndExpression => SyntaxKind.AmpersandAmpersandToken,
            SyntaxKind.BitwiseOrExpression => SyntaxKind.BarToken,
            SyntaxKind.BitwiseAndExpression => SyntaxKind.AmpersandToken,
            SyntaxKind.ExclusiveOrExpression => SyntaxKind.CaretToken,
            SyntaxKind.EqualsExpression => SyntaxKind.EqualsEqualsToken,
            SyntaxKind.NotEqualsExpression => SyntaxKind.ExclamationEqualsToken,
            SyntaxKind.LessThanExpression => SyntaxKind.LessThanToken,
            SyntaxKind.LessThanOrEqualExpression => SyntaxKind.LessThanEqualsToken,
            SyntaxKind.GreaterThanExpression => SyntaxKind.GreaterThanToken,
            SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.GreaterThanEqualsToken,
            SyntaxKind.IsExpression => SyntaxKind.IsKeyword,
            SyntaxKind.AsExpression => SyntaxKind.AsKeyword,
            SyntaxKind.CoalesceExpression => SyntaxKind.QuestionQuestionToken,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not in switch statement."),
        };
    }

    private static SyntaxKind GetMemberAccessExpressionOperatorTokenKind(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.SimpleMemberAccessExpression => SyntaxKind.DotToken,
            SyntaxKind.PointerMemberAccessExpression => SyntaxKind.MinusGreaterThanToken,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static SyntaxKind GetAssignmentExpressionOperatorTokenKind(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.SimpleAssignmentExpression => SyntaxKind.EqualsToken,
            SyntaxKind.AddAssignmentExpression => SyntaxKind.PlusEqualsToken,
            SyntaxKind.SubtractAssignmentExpression => SyntaxKind.MinusEqualsToken,
            SyntaxKind.MultiplyAssignmentExpression => SyntaxKind.AsteriskEqualsToken,
            SyntaxKind.DivideAssignmentExpression => SyntaxKind.SlashEqualsToken,
            SyntaxKind.ModuloAssignmentExpression => SyntaxKind.PercentEqualsToken,
            SyntaxKind.AndAssignmentExpression => SyntaxKind.AmpersandEqualsToken,
            SyntaxKind.ExclusiveOrAssignmentExpression => SyntaxKind.CaretEqualsToken,
            SyntaxKind.OrAssignmentExpression => SyntaxKind.BarEqualsToken,
            SyntaxKind.LeftShiftAssignmentExpression => SyntaxKind.LessThanLessThanEqualsToken,
            SyntaxKind.RightShiftAssignmentExpression => SyntaxKind.GreaterThanGreaterThanEqualsToken,
            SyntaxKind.CoalesceAssignmentExpression => SyntaxKind.QuestionQuestionEqualsToken,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not in switch statement"),
        };
    }

    private static SyntaxKind GetPrefixUnaryExpressionOperatorTokenKind(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.UnaryPlusExpression => SyntaxKind.PlusToken,
            SyntaxKind.UnaryMinusExpression => SyntaxKind.MinusToken,
            SyntaxKind.BitwiseNotExpression => SyntaxKind.TildeToken,
            SyntaxKind.LogicalNotExpression => SyntaxKind.ExclamationToken,
            SyntaxKind.PreIncrementExpression => SyntaxKind.PlusPlusToken,
            SyntaxKind.PreDecrementExpression => SyntaxKind.MinusMinusToken,
            SyntaxKind.AddressOfExpression => SyntaxKind.AmpersandToken,
            SyntaxKind.PointerIndirectionExpression => SyntaxKind.AsteriskToken,
            SyntaxKind.IndexExpression => SyntaxKind.CaretToken,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static SyntaxKind GetPostfixUnaryExpressionOperatorTokenKind(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.PostIncrementExpression => SyntaxKind.PlusPlusToken,
            SyntaxKind.PostDecrementExpression => SyntaxKind.MinusMinusToken,
            SyntaxKind.SuppressNullableWarningExpression => SyntaxKind.ExclamationToken,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static SyntaxKind GetConstructorInitializerThisOrBaseKeywordKind(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.BaseConstructorInitializer => SyntaxKind.BaseKeyword,
            SyntaxKind.ThisConstructorInitializer => SyntaxKind.ThisKeyword,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static SyntaxKind GetAccessorDeclarationKeywordKind(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.GetAccessorDeclaration => SyntaxKind.GetKeyword,
            SyntaxKind.SetAccessorDeclaration => SyntaxKind.SetKeyword,
            SyntaxKind.InitAccessorDeclaration => SyntaxKind.InitKeyword,
            SyntaxKind.AddAccessorDeclaration => SyntaxKind.AddKeyword,
            SyntaxKind.RemoveAccessorDeclaration => SyntaxKind.RemoveKeyword,
            SyntaxKind.UnknownAccessorDeclaration => SyntaxKind.IdentifierToken,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static SyntaxKind GetLiteralExpressionTokenKind(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.ArgListExpression => SyntaxKind.ArgListKeyword,
            SyntaxKind.NumericLiteralExpression => SyntaxKind.NumericLiteralToken,
            SyntaxKind.StringLiteralExpression => SyntaxKind.StringLiteralToken,
            SyntaxKind.CharacterLiteralExpression => SyntaxKind.CharacterLiteralToken,
            SyntaxKind.TrueLiteralExpression => SyntaxKind.TrueKeyword,
            SyntaxKind.FalseLiteralExpression => SyntaxKind.FalseKeyword,
            SyntaxKind.NullLiteralExpression => SyntaxKind.NullKeyword,
            SyntaxKind.DefaultLiteralExpression => SyntaxKind.DefaultKeyword,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static SyntaxKind GetBinaryPatternOperatorTokenKind(SyntaxKind kind)
        => kind switch
        {
            SyntaxKind.OrPattern => SyntaxKind.OrKeyword,
            SyntaxKind.AndPattern => SyntaxKind.AndKeyword,
            _ => throw new ArgumentOutOfRangeException(),
        };

    private static SyntaxToken XmlReplaceBracketTokens(SyntaxToken originalToken, SyntaxToken rewrittenToken)
    {
        if (rewrittenToken.IsKind(SyntaxKind.LessThanToken) && string.Equals("<", rewrittenToken.Text, StringComparison.Ordinal))
        {
            return Token(rewrittenToken.LeadingTrivia, SyntaxKind.LessThanToken, "{", rewrittenToken.ValueText, rewrittenToken.TrailingTrivia);
        }

        if (rewrittenToken.IsKind(SyntaxKind.GreaterThanToken) && string.Equals(">", rewrittenToken.Text, StringComparison.Ordinal))
        {
            return Token(rewrittenToken.LeadingTrivia, SyntaxKind.GreaterThanToken, "}", rewrittenToken.ValueText, rewrittenToken.TrailingTrivia);
        }

        return rewrittenToken;
    }
}
