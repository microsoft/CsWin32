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
        CLSCTX.CLSCTX_INPROC_SERVER,
        out ISpellCheckerFactory spellCheckerFactory).ThrowOnFailure();

    BOOL supported = spellCheckerFactory.IsSupported("en-US");

    if (!supported)
    {
        return;
    }

    ISpellChecker spellChecker = spellCheckerFactory.CreateSpellChecker("en-US");

    var text = @"""Cann I I haev some?""";

    Console.WriteLine(@"Check {0}", text);

    IEnumSpellingError errors = spellChecker.Check(text);

    Span<PWSTR> suggestionResult = new PWSTR[1];
    while (true)
    {
        if (errors.Next() is not ISpellingError error)
        {
            break;
        }

        uint startIndex = error.get_StartIndex();
        uint length = error.get_Length();

        var word = text.Substring((int)startIndex, (int)length);

        CORRECTIVE_ACTION action = error.get_CorrectiveAction();

        switch (action)
        {
            case CORRECTIVE_ACTION.CORRECTIVE_ACTION_DELETE:
                Console.WriteLine(@"Delete ""{0}""", word);
                break;
            case CORRECTIVE_ACTION.CORRECTIVE_ACTION_REPLACE:
                PWSTR replacement = error.get_Replacement();
                Console.WriteLine(@"Replace ""{0}"" with ""{1}""", word, replacement);
                CoTaskMemFree(replacement);
                break;
            case CORRECTIVE_ACTION.CORRECTIVE_ACTION_GET_SUGGESTIONS:
                Console.WriteLine(@"Suggest replacing ""{0}"" with:", word);
                IEnumString suggestions = spellChecker.Suggest(word);
                do
                {
                    suggestions.Next(suggestionResult, null);
                    if (suggestionResult[0].Value is not null)
                    {
                        Console.WriteLine($"\t{suggestionResult[0]}");
                        CoTaskMemFree(suggestionResult[0]);
                    }
                }
                while (suggestionResult[0].Value is not null);

                break;
            default:
                break;
        }
    }
}
