// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.Windows.CsWin32.FastSyntaxFactory;

namespace Microsoft.Windows.CsWin32;

public partial class Generator
{
    private static string? FetchTemplateText(string name)
    {
        using Stream? templateStream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{ThisAssembly.RootNamespace}.templates.{name.Replace('/', '.')}.cs");
        if (templateStream is null)
        {
            return null;
        }

        using StreamReader sr = new(templateStream);
        return sr.ReadToEnd().Replace("\r\n", "\n").Replace("\t", string.Empty);
    }

    private static bool TryFetchTemplate(string name, Generator? generator, [NotNullWhen(true)] out MemberDeclarationSyntax? member)
    {
        string? template = FetchTemplateText(name);
        if (template == null)
        {
            member = null;
            return false;
        }

        member = ParseMemberDeclaration(template, generator?.parseOptions) ?? throw new GenerationFailedException($"Unable to parse a type from a template: {name}");

        // Strip out #if/#else/#endif trivia, which was already evaluated with the parse options we passed in.
        if (generator?.parseOptions is not null)
        {
            member = (MemberDeclarationSyntax)member.Accept(DirectiveTriviaRemover.Instance)!;
        }

        member = generator?.ElevateVisibility(member) ?? member;
        return true;
    }

    private IEnumerable<MemberDeclarationSyntax> ExtractMembersFromTemplate(string name) => ((TypeDeclarationSyntax)this.FetchTemplate($"{name}")).Members;

    /// <summary>
    /// Promotes an <see langword="internal" /> member to be <see langword="public"/> if <see cref="Visibility"/> indicates that generated APIs should be public.
    /// This change is applied recursively.
    /// </summary>
    /// <param name="member">The member to potentially make public.</param>
    /// <returns>The modified or original <paramref name="member"/>.</returns>
    private MemberDeclarationSyntax ElevateVisibility(MemberDeclarationSyntax member)
    {
        if (this.Visibility == SyntaxKind.PublicKeyword)
        {
            MemberDeclarationSyntax publicMember = member;
            int indexOfInternal = publicMember.Modifiers.IndexOf(SyntaxKind.InternalKeyword);
            if (indexOfInternal >= 0)
            {
                publicMember = publicMember.WithModifiers(publicMember.Modifiers.Replace(publicMember.Modifiers[indexOfInternal], TokenWithSpace(this.Visibility)));
            }

            // Apply change recursively.
            if (publicMember is TypeDeclarationSyntax memberContainer)
            {
                publicMember = memberContainer.WithMembers(List(memberContainer.Members.Select(this.ElevateVisibility)));
            }

            return publicMember;
        }

        return member;
    }

    private MemberDeclarationSyntax FetchTemplate(string name)
    {
        if (!this.TryFetchTemplate(name, out MemberDeclarationSyntax? result))
        {
            throw new KeyNotFoundException();
        }

        return result;
    }

    private bool TryFetchTemplate(string name, [NotNullWhen(true)] out MemberDeclarationSyntax? member) => TryFetchTemplate(name, this, out member);
}
