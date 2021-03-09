// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Microsoft.Windows.Sdk;
using static Microsoft.Windows.Sdk.Constants;
using static Microsoft.Windows.Sdk.PInvoke;

unsafe
{
    CoCreateInstance(
        typeof(SpellCheckerFactory).GUID,
        null,
        (uint)CLSCTX.CLSCTX_INPROC_SERVER, // https://github.com/microsoft/win32metadata/issues/185
        typeof(ISpellCheckerFactory).GUID,
        out ISpellCheckerFactory* spellCheckerFactory).ThrowOnFailure();

    spellCheckerFactory->IsSupported(
        "en-US",
        out BOOL supported).ThrowOnFailure();

    if (!supported)
    {
        return;
    }

    spellCheckerFactory->CreateSpellChecker(
        "en-US",
        out ISpellChecker* spellChecker).ThrowOnFailure();

    var text = @"""Cann I I haev some?""";

    Console.WriteLine(@"Check {0}", text);

    spellChecker->Check(
        text,
        out IEnumSpellingError* errors).ThrowOnFailure();

    Span<PWSTR> suggestionResult = new PWSTR[1];
    while (true)
    {
        if (errors->Next(out ISpellingError* error).ThrowOnFailure() == S_FALSE)
        {
            break;
        }

        error->get_StartIndex(out uint startIndex).ThrowOnFailure();
        error->get_Length(out uint length).ThrowOnFailure();

        var word = text.Substring((int)startIndex, (int)length);

        error->get_CorrectiveAction(out CORRECTIVE_ACTION action).ThrowOnFailure();

        switch (action)
        {
            case CORRECTIVE_ACTION.CORRECTIVE_ACTION_DELETE:
                Console.WriteLine(@"Delete ""{0}""", word);
                break;
            case CORRECTIVE_ACTION.CORRECTIVE_ACTION_REPLACE:
                error->get_Replacement(out PWSTR replacement).ThrowOnFailure();
                Console.WriteLine(@"Replace ""{0}"" with ""{1}""", word, replacement);
                CoTaskMemFree(replacement);
                break;
            case CORRECTIVE_ACTION.CORRECTIVE_ACTION_GET_SUGGESTIONS:
                var l = new List<string>();
                spellChecker->Suggest(word, out IEnumString* suggestions).ThrowOnFailure();
                while (true)
                {
                    if (suggestions->Next(suggestionResult, null).ThrowOnFailure() != S_OK)
                    {
                        break;
                    }

                    l.Add(suggestionResult[0].ToString());
                    CoTaskMemFree(suggestionResult[0]);
                }

                suggestions->Release();
                Console.WriteLine(@"Suggest replacing ""{0}"" with:", word);
                foreach (var s in l)
                {
                    Console.WriteLine("\t{0}", s);
                }

                break;
            default:
                break;
        }

        error->Release();
    }

    errors->Release();
    spellChecker->Release();
}
