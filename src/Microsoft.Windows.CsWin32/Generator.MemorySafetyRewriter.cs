// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    /// <summary>
    /// Rewrites generated code so that it compiles without errors or warnings under the
    /// <see href="https://learn.microsoft.com/dotnet/csharp/language-reference/unsafe-code#the-updated-memory-safety-model-preview">updated
    /// memory safety model</see> that is in preview in C# 15 / .NET 11.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Under the original model, <see langword="unsafe"/> merely establishes a context in which pointer features may
    /// appear, and it places no requirement on callers. The updated model splits the keyword into two roles — a
    /// contract that propagates an audit obligation to callers, and a block that scopes the operations which access
    /// unmanaged memory — and that turns several things which are legal today into errors:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see langword="unsafe"/> on a type or delegate declaration is an error (CS9377), because
    /// there is no caller for the modifier to inform.</description></item>
    /// <item><description>Every <see langword="extern"/> member (including a <c>LibraryImport</c> partial method) must
    /// be marked <see langword="unsafe"/> or <c>safe</c> (CS9389).</description></item>
    /// <item><description>Every instance field of a type with an explicit layout must be marked <see langword="unsafe"/>
    /// or <c>safe</c> (CS9392).</description></item>
    /// <item><description><see langword="unsafe"/> on a member signature no longer establishes an unsafe context for
    /// that member's body, so pointer operations in the body need their own <see langword="unsafe"/> block
    /// (CS9360).</description></item>
    /// <item><description>Calling a member marked <see langword="unsafe"/> requires an unsafe context at the call site
    /// (CS9362), and an <see langword="unsafe"/> member may not override or implement a safe one (CS9364/CS9366).</description></item>
    /// </list>
    /// <para>
    /// The rewriter applies the following rules to the final syntax trees:
    /// </para>
    /// <list type="number">
    /// <item><description>Strip <see langword="unsafe"/> from type and delegate declarations, and from fields. Holding
    /// a pointer is a safe operation under the updated model, so a pointer-typed field needs no modifier — and leaving
    /// one off keeps the field readable and writable from safe code, exactly as it is today.</description></item>
    /// <item><description>Mark instance fields of explicitly laid out types (the unions in the projection) <c>safe</c>.</description></item>
    /// <item><description>Mark each <see langword="extern"/>-like member <see langword="unsafe"/> when its signature
    /// mentions a pointer, and <c>safe</c> otherwise.</description></item>
    /// <item><description>For every other member, carry <see langword="unsafe"/> if and only if its signature mentions
    /// a pointer. This matches the obligation the compiler already infers for a pointer-bearing signature when the
    /// updated rules are off but the language version is <c>preview</c> (CS9363), so a call site's obligations don't
    /// change with the opt-in. It also keeps <see langword="unsafe"/> off of overrides and interface implementations of
    /// safe members (such as <see cref="object.ToString"/> or <see cref="SafeHandle.ReleaseHandle"/>), whose signatures
    /// never mention pointers.</description></item>
    /// <item><description>Establish an unsafe context inside every member that has one to establish: wrap each body in
    /// an inner <see langword="unsafe"/> block (converting an expression body to a block body), and wrap each field or
    /// property initializer and constructor-initializer argument — positions where a block can't appear — in an
    /// <c>unsafe(…)</c> expression.</description></item>
    /// </list>
    /// <para>
    /// That last rule applies to every member rather than only to the ones that were in an unsafe context before,
    /// because deciding which bodies truly need a context requires semantic information that isn't available while the
    /// code is being generated. Two things make the broad approach the right one: a redundant <see langword="unsafe"/>
    /// block or expression produces no diagnostic, and .NET 11 marks a wide set of runtime members that the projection
    /// depends on as caller-unsafe — <see cref="System.Runtime.CompilerServices.Unsafe"/> and
    /// <c>MemoryMarshal.CreateSpan</c> among them — so members that were entirely safe under the original model
    /// need a context now too.
    /// </para>
    /// </remarks>
    private class MemorySafetyRewriter : CSharpSyntaxRewriter
    {
        /// <summary>
        /// The <c>safe</c> contextual keyword. It has no <see cref="SyntaxKind"/> in the Roslyn versions this
        /// generator compiles against, so it is spelled as an identifier token. That only affects how the token is
        /// represented in our own syntax tree; the C# 15 compiler that consumes the generated text parses it as the
        /// contextual keyword.
        /// </summary>
        private static readonly SyntaxToken SafeKeyword = Identifier("safe").WithTrailingTrivia(Space);

        /// <summary>
        /// Tracks, for each enclosing type declaration, whether it has an explicit (or extended) layout, in which
        /// case each of its instance fields must be marked <see langword="unsafe"/> or <c>safe</c>.
        /// </summary>
        private readonly Stack<bool> explicitLayoutContext = new();

        internal MemorySafetyRewriter()
            : base(visitIntoStructuredTrivia: false)
        {
        }

        private bool InExplicitLayoutContext => this.explicitLayoutContext.Count > 0 && this.explicitLayoutContext.Peek();

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            using var context = new TypeContext(this, HasExplicitLayout(node.AttributeLists));
            return base.VisitClassDeclaration(node.WithModifiers(StripUnsafe(node.Modifiers)));
        }

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        {
            using var context = new TypeContext(this, HasExplicitLayout(node.AttributeLists));
            return base.VisitStructDeclaration(node.WithModifiers(StripUnsafe(node.Modifiers)));
        }

        public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            using var context = new TypeContext(this, explicitLayout: false);
            return base.VisitInterfaceDeclaration(node.WithModifiers(StripUnsafe(node.Modifiers)));
        }

        public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            using var context = new TypeContext(this, HasExplicitLayout(node.AttributeLists));
            return base.VisitRecordDeclaration(node.WithModifiers(StripUnsafe(node.Modifiers)));
        }

        public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            using var context = new TypeContext(this, explicitLayout: false);
            return base.VisitEnumDeclaration(node);
        }

        public override SyntaxNode? VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            // A delegate is type-shaped, so it can carry no safety obligation for its callers.
            return base.VisitDelegateDeclaration(node.WithModifiers(StripUnsafe(node.Modifiers)));
        }

        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            SyntaxTokenList modifiers = StripUnsafe(node.Modifiers);

            // Only an instance field of an explicitly laid out type requires an explicit safety attestation.
            if (this.InExplicitLayoutContext && !modifiers.Any(SyntaxKind.ConstKeyword) && !modifiers.Any(SyntaxKind.StaticKeyword))
            {
                modifiers = AddSafe(modifiers);
            }

            node = node.WithModifiers(modifiers);

            // A `const` initializer is a constant expression, which can never reach a caller-unsafe member.
            if (!modifiers.Any(SyntaxKind.ConstKeyword))
            {
                node = node.WithDeclaration(WrapInitializers(node.Declaration));
            }

            return base.VisitFieldDeclaration(node);
        }

        public override SyntaxNode? VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
        {
            return base.VisitEventFieldDeclaration(node.WithModifiers(StripUnsafe(node.Modifiers)));
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            bool pointerInSignature = RequiresUnsafe(node.ReturnType) || SignatureRequiresUnsafe(node.ParameterList);
            return base.VisitMethodDeclaration((MethodDeclarationSyntax)RewriteMember(node, pointerInSignature));
        }

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            // A constructor is never marked caller-unsafe, even when it takes a pointer. Nothing can discharge that
            // obligation at a `this(…)`/`base(…)` initializer — the initializer call sits outside any block, and an
            // `unsafe(…)` expression can only cover the arguments — so a caller-unsafe constructor would be
            // unreachable from a chained constructor. The projection's pointer-taking constructors only store the
            // pointer in a field, which the updated model treats as a safe operation anyway.
            node = (ConstructorDeclarationSyntax)RewriteMember(node, pointerInSignature: false);

            // A constructor initializer runs before the body, so an inner block can't cover its arguments, which may
            // still invoke a caller-unsafe member such as a pointer-taking conversion operator.
            if (node.Initializer is ConstructorInitializerSyntax initializer)
            {
                node = node.WithInitializer(initializer.WithArgumentList(WrapArguments(initializer.ArgumentList)));
            }

            return base.VisitConstructorDeclaration(node);
        }

        public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            bool pointerInSignature = RequiresUnsafe(node.ReturnType) || SignatureRequiresUnsafe(node.ParameterList);
            return base.VisitOperatorDeclaration((OperatorDeclarationSyntax)RewriteMember(node, pointerInSignature));
        }

        public override SyntaxNode? VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            bool pointerInSignature = RequiresUnsafe(node.Type) || SignatureRequiresUnsafe(node.ParameterList);
            return base.VisitConversionOperatorDeclaration((ConversionOperatorDeclarationSyntax)RewriteMember(node, pointerInSignature));
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            SyntaxTokenList modifiers = StripUnsafe(node.Modifiers);
            if (RequiresUnsafe(node.Type))
            {
                modifiers = AddUnsafe(modifiers);
            }

            node = node.WithModifiers(modifiers);

            if (node.ExpressionBody is ArrowExpressionClauseSyntax arrow)
            {
                // T P => expr;   ==>   T P { get { unsafe { return expr; } } }
                node = node
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithIdentifier(node.Identifier.WithTrailingTrivia(LineFeed))
                    .WithAccessorList(AccessorList(GetAccessorFromExpression(arrow.Expression, node.Type)));
            }
            else if (node.Initializer is EqualsValueClauseSyntax initializer)
            {
                node = node.WithInitializer(initializer.WithValue(UnsafeExpression(initializer.Value, node.Type)));
            }

            return base.VisitPropertyDeclaration(node);
        }

        public override SyntaxNode? VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            SyntaxTokenList modifiers = StripUnsafe(node.Modifiers);
            if (RequiresUnsafe(node.Type) || SignatureRequiresUnsafe(node.ParameterList))
            {
                modifiers = AddUnsafe(modifiers);
            }

            node = node.WithModifiers(modifiers);

            if (node.ExpressionBody is ArrowExpressionClauseSyntax arrow)
            {
                node = node
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithParameterList(node.ParameterList.WithTrailingTrivia(LineFeed))
                    .WithAccessorList(AccessorList(GetAccessorFromExpression(arrow.Expression, node.Type)));
            }

            return base.VisitIndexerDeclaration(node);
        }

        public override SyntaxNode? VisitEventDeclaration(EventDeclarationSyntax node)
        {
            SyntaxTokenList modifiers = StripUnsafe(node.Modifiers);
            if (RequiresUnsafe(node.Type))
            {
                modifiers = AddUnsafe(modifiers);
            }

            return base.VisitEventDeclaration(node.WithModifiers(modifiers));
        }

        public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            bool isVoid = node.Keyword.IsKind(SyntaxKind.SetKeyword)
                || node.Keyword.IsKind(SyntaxKind.InitKeyword)
                || node.Keyword.IsKind(SyntaxKind.AddKeyword)
                || node.Keyword.IsKind(SyntaxKind.RemoveKeyword);

            if (node.Body is BlockSyntax body)
            {
                node = node.WithBody(WrapInUnsafeBlock(body));
            }
            else if (node.ExpressionBody is ArrowExpressionClauseSyntax arrow)
            {
                node = node
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithKeyword(node.Keyword.WithTrailingTrivia(LineFeed))
                    .WithBody(WrapInUnsafeBlock(BlockFromExpression(arrow.Expression, isVoid, isRef: false)));
            }

            return base.VisitAccessorDeclaration(node);
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            // The extern local functions that wrap a P/Invoke need the same attestation a member-level one does.
            // Their containing member's unsafe block already covers everything else about them.
            if (IsExternLike(node.Modifiers, node.AttributeLists, node.Body, node.ExpressionBody))
            {
                bool pointerInSignature = RequiresUnsafe(node.ReturnType) || SignatureRequiresUnsafe(node.ParameterList);
                SyntaxTokenList modifiers = StripUnsafe(node.Modifiers);
                node = node.WithModifiers(pointerInSignature ? AddUnsafe(modifiers) : AddSafe(modifiers));
            }

            return base.VisitLocalFunctionStatement(node);
        }

        /// <summary>
        /// Determines whether a type declaration is laid out explicitly, which requires each of its instance fields
        /// to be marked <see langword="unsafe"/> or <c>safe</c>.
        /// </summary>
        /// <param name="attributeLists">The attribute lists on the type declaration.</param>
        /// <returns><see langword="true"/> if the type has an explicit layout.</returns>
        private static bool HasExplicitLayout(SyntaxList<AttributeListSyntax> attributeLists)
        {
            foreach (AttributeListSyntax list in attributeLists)
            {
                foreach (AttributeSyntax attribute in list.Attributes)
                {
                    if (GetSimpleAttributeName(attribute) is not ("StructLayout" or "StructLayoutAttribute"))
                    {
                        continue;
                    }

                    foreach (AttributeArgumentSyntax argument in attribute.ArgumentList?.Arguments ?? default)
                    {
                        // The layout kind is the attribute's only positional argument, spelled as `LayoutKind.Explicit`
                        // with whatever qualification the generator used.
                        if (argument.NameEquals is null && argument.Expression.ToString().EndsWith("Explicit", StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static string GetSimpleAttributeName(AttributeSyntax attribute) => attribute.Name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => string.Empty,
        };

        private static bool SignatureRequiresUnsafe(BaseParameterListSyntax? parameterList)
        {
            foreach (ParameterSyntax parameter in parameterList?.Parameters ?? default)
            {
                if (RequiresUnsafe(parameter.Type))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether a member is implemented outside of C#, which under the updated rules obliges the author
        /// to attest to the member's safety.
        /// </summary>
        /// <param name="modifiers">The member's modifiers.</param>
        /// <param name="attributeLists">The member's attributes.</param>
        /// <param name="body">The member's block body, if any.</param>
        /// <param name="expressionBody">The member's expression body, if any.</param>
        /// <returns><see langword="true"/> if the member is <see langword="extern"/> or a <c>LibraryImport</c> partial method.</returns>
        private static bool IsExternLike(SyntaxTokenList modifiers, SyntaxList<AttributeListSyntax> attributeLists, BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody)
        {
            if (modifiers.Any(SyntaxKind.ExternKeyword))
            {
                return true;
            }

            // A `LibraryImport` partial method has no body either, and the compiler treats it like an extern member.
            if (!modifiers.Any(SyntaxKind.PartialKeyword) || body is not null || expressionBody is not null)
            {
                return false;
            }

            foreach (AttributeListSyntax list in attributeLists)
            {
                foreach (AttributeSyntax attribute in list.Attributes)
                {
                    if (GetSimpleAttributeName(attribute) is "LibraryImport" or "LibraryImportAttribute" or "DllImport" or "DllImportAttribute")
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static SyntaxTokenList StripUnsafe(SyntaxTokenList modifiers)
        {
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                if (modifiers[i].IsKind(SyntaxKind.UnsafeKeyword))
                {
                    modifiers = modifiers.RemoveAt(i);
                }
            }

            return modifiers;
        }

        private static SyntaxTokenList AddUnsafe(SyntaxTokenList modifiers) => AddModifier(modifiers, TokenWithSpace(SyntaxKind.UnsafeKeyword));

        private static SyntaxTokenList AddSafe(SyntaxTokenList modifiers) => AddModifier(modifiers, SafeKeyword);

        /// <summary>
        /// Appends a modifier, keeping it ahead of <see langword="partial"/>, which C# requires to come last.
        /// </summary>
        /// <param name="modifiers">The existing modifiers.</param>
        /// <param name="modifier">The modifier to add.</param>
        /// <returns>The new modifier list.</returns>
        private static SyntaxTokenList AddModifier(SyntaxTokenList modifiers, SyntaxToken modifier)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].IsKind(SyntaxKind.PartialKeyword))
                {
                    return modifiers.Insert(i, modifier);
                }
            }

            return modifiers.Add(modifier);
        }

        /// <summary>
        /// Drops the space that the removed <c>=&gt;</c> left on the signature, now that the body starts on its own
        /// line.
        /// </summary>
        /// <param name="node">A member whose expression body was just replaced with a block body.</param>
        /// <returns>The member with that trailing space removed.</returns>
        private static BaseMethodDeclarationSyntax TrimTriviaBeforeBody(BaseMethodDeclarationSyntax node)
        {
            SyntaxToken tokenBeforeBody = node.Body!.OpenBraceToken.GetPreviousToken();
            return node.ReplaceToken(tokenBeforeBody, tokenBeforeBody.WithTrailingTrivia());
        }

        private static BlockSyntax WrapInUnsafeBlock(BlockSyntax body)
        {
            // Avoid `unsafe { unsafe { ... } }` where the code already scopes its whole body.
            if (body.Statements is [UnsafeStatementSyntax])
            {
                return body;
            }

            return Block(UnsafeStatement(body));
        }

        private static AccessorDeclarationSyntax GetAccessorFromExpression(ExpressionSyntax expression, TypeSyntax propertyType) =>
            AccessorDeclaration(
                SyntaxKind.GetAccessorDeclaration,
                WrapInUnsafeBlock(BlockFromExpression(expression, isVoid: false, isRef: propertyType is RefTypeSyntax)));

        private static BlockSyntax BlockFromExpression(ExpressionSyntax expression, bool isVoid, bool isRef)
        {
            // A `throw` expression has no value to return, so it becomes a throw statement.
            if (expression is ThrowExpressionSyntax @throw)
            {
                return Block(ThrowStatement(@throw.Expression.WithoutTrailingTrivia()));
            }

            expression = expression.WithoutTrailingTrivia();

            if (isVoid)
            {
                return Block(ExpressionStatement(expression));
            }

            // A ref-returning arrow body spells the `ref` itself (`=> ref x`), so the expression is already a
            // RefExpressionSyntax that carries over to the return statement unchanged.
            return Block(isRef && expression is not RefExpressionSyntax
                ? ReturnStatement(RefExpression(expression))
                : ReturnStatement(expression));
        }

        private static VariableDeclarationSyntax WrapInitializers(VariableDeclarationSyntax declaration)
        {
            SeparatedSyntaxList<VariableDeclaratorSyntax> variables = declaration.Variables;
            for (int i = 0; i < variables.Count; i++)
            {
                if (variables[i].Initializer is EqualsValueClauseSyntax initializer)
                {
                    variables = variables.Replace(
                        variables[i],
                        variables[i].WithInitializer(initializer.WithValue(UnsafeExpression(initializer.Value, declaration.Type))));
                }
            }

            return declaration.WithVariables(variables);
        }

        private static ArgumentListSyntax WrapArguments(ArgumentListSyntax argumentList)
        {
            SeparatedSyntaxList<ArgumentSyntax> arguments = argumentList.Arguments;
            for (int i = 0; i < arguments.Count; i++)
            {
                // A `ref`/`out`/`in` argument is a variable reference rather than a value, so it can't be wrapped.
                // It also invokes nothing, so it needs no context of its own.
                if (arguments[i].RefKindKeyword.IsKind(SyntaxKind.None))
                {
                    arguments = arguments.Replace(arguments[i], arguments[i].WithExpression(UnsafeExpression(arguments[i].Expression, targetType: null)));
                }
            }

            return argumentList.WithArguments(arguments);
        }

        /// <summary>
        /// Creates an <c>unsafe(expression)</c> expression, which establishes an unsafe context for a single
        /// expression in a position where an <see langword="unsafe"/> block can't appear.
        /// </summary>
        /// <param name="expression">The expression to evaluate in an unsafe context.</param>
        /// <param name="targetType">
        /// The type the expression is being converted to, when it is known. The unsafe context ends at the closing
        /// parenthesis, so an implicit user-defined conversion applied to the result would fall outside it — and the
        /// projection's pointer-taking conversion operators are caller-unsafe. Naming the conversion explicitly inside
        /// the parentheses brings it back in; an implicit conversion is always available explicitly, so adding the cast
        /// never changes which conversion runs.
        /// </param>
        /// <returns>The wrapped expression.</returns>
        /// <remarks>
        /// The Roslyn versions this generator compiles against have no syntax node for this C# 15 expression, so it is
        /// composed as an invocation of an identifier spelled <c>unsafe</c>. Only the generated text matters; the
        /// C# 15 compiler that consumes it parses that text as an unsafe expression.
        /// </remarks>
        private static ExpressionSyntax UnsafeExpression(ExpressionSyntax expression, TypeSyntax? targetType)
        {
            ExpressionSyntax inner = expression.WithoutTrivia();
            if (targetType is not null && CanCastTo(targetType, inner) && !CastsTo(inner, targetType))
            {
                inner = CastExpression(targetType.WithoutTrivia(), ParenthesizedExpression(inner));
            }

            return InvocationExpression(IdentifierName(Identifier("unsafe")), [Argument(inner)])
                .WithTriviaFrom(expression);
        }

        /// <summary>
        /// Determines whether an expression already spells out a cast to the given type, in which case adding another
        /// one would just be noise.
        /// </summary>
        /// <param name="expression">The initializer expression.</param>
        /// <param name="targetType">The declared type of the variable being initialized.</param>
        /// <returns><see langword="true"/> if the expression is a cast to that same type.</returns>
        private static bool CastsTo(ExpressionSyntax expression, TypeSyntax targetType) =>
            expression is CastExpressionSyntax cast && cast.Type.WithoutTrivia().IsEquivalentTo(targetType.WithoutTrivia());

        /// <summary>
        /// Determines whether a cast to the given type can be written in front of the given expression.
        /// </summary>
        /// <param name="targetType">The declared type of the variable being initialized.</param>
        /// <param name="expression">The initializer expression.</param>
        /// <returns><see langword="false"/> for a declared type that isn't spelled out, and for the expression forms
        /// that take their type from their context and so can't be preceded by a cast.</returns>
        private static bool CanCastTo(TypeSyntax targetType, ExpressionSyntax expression)
        {
            if (targetType is RefTypeSyntax || targetType.IsVar)
            {
                return false;
            }

            return expression is not (
                InitializerExpressionSyntax or
                ImplicitArrayCreationExpressionSyntax or
                ImplicitObjectCreationExpressionSyntax or
                ImplicitStackAllocArrayCreationExpressionSyntax or
                AnonymousFunctionExpressionSyntax);
        }

        /// <summary>
        /// Applies the modifier and body rules that are common to methods, constructors, and operators.
        /// </summary>
        /// <param name="node">The member to rewrite.</param>
        /// <param name="pointerInSignature">Whether the member's signature mentions a pointer type.</param>
        /// <returns>The rewritten member.</returns>
        private static BaseMethodDeclarationSyntax RewriteMember(BaseMethodDeclarationSyntax node, bool pointerInSignature)
        {
            SyntaxTokenList modifiers = StripUnsafe(node.Modifiers);

            if (IsExternLike(node.Modifiers, node.AttributeLists, node.Body, node.ExpressionBody))
            {
                // The compiler can't classify a call into native code, so it makes us say which it is.
                return node.WithModifiers(pointerInSignature ? AddUnsafe(modifiers) : AddSafe(modifiers));
            }

            node = node.WithModifiers(pointerInSignature ? AddUnsafe(modifiers) : modifiers);

            if (node.Body is BlockSyntax body)
            {
                return node.WithBody(WrapInUnsafeBlock(body));
            }

            if (node.ExpressionBody is ArrowExpressionClauseSyntax arrow)
            {
                // Constructors and destructors evaluate their arrow body for effect. Everything else produces a value,
                // including operators and conversion operators, which aren't MethodDeclarationSyntax.
                bool isVoid = node switch
                {
                    MethodDeclarationSyntax method => method.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword },
                    OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => false,
                    _ => true,
                };
                bool isRef = node is MethodDeclarationSyntax { ReturnType: RefTypeSyntax };
                node = node
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(WrapInUnsafeBlock(BlockFromExpression(arrow.Expression, isVoid, isRef)));
                return TrimTriviaBeforeBody(node);
            }

            return node;
        }

        /// <summary>
        /// Pushes the explicit-layout state for a type declaration for the duration of its visit.
        /// </summary>
        private struct TypeContext : IDisposable
        {
            private readonly MemorySafetyRewriter rewriter;

            internal TypeContext(MemorySafetyRewriter rewriter, bool explicitLayout)
            {
                this.rewriter = rewriter;
                rewriter.explicitLayoutContext.Push(explicitLayout);
            }

            public void Dispose() => this.rewriter.explicitLayoutContext.Pop();
        }
    }
}
