// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Globalization;
using Windows.Win32.System.Com;
using static Windows.Win32.PInvoke;

unsafe
{
    var spellCheckerFactory = (ISpellCheckerFactory)new SpellCheckerFactory();

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
        if (errors.Next(out ISpellingError error).ThrowOnFailure() == HRESULT.S_FALSE)
        {
            break;
        }

        uint startIndex = error.StartIndex;
        uint length = error.Length;

        var word = text.Substring((int)startIndex, (int)length);

        CORRECTIVE_ACTION action = error.CorrectiveAction;

        switch (action)
        {
            case CORRECTIVE_ACTION.CORRECTIVE_ACTION_DELETE:
                Console.WriteLine(@"Delete ""{0}""", word);
                break;
            case CORRECTIVE_ACTION.CORRECTIVE_ACTION_REPLACE:
                PWSTR replacement = error.Replacement;
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
