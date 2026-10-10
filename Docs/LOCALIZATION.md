# Localization maintenance

The expansion uses RimWorld's native Keyed and DefInjected translation loading. Language folders are ChineseSimplified, English, Russian, Japanese and French. RimWorld also accepts these legacy names when its installed language directories include native names in parentheses.

Runtime strings use Source/NmcText.cs. Format arguments remain positional .NET placeholders, including alignment and number formats. Def names, save keys, IDs and synchronized pawn names remain invariant. Previously saved quest text follows RimWorld's saved-text behavior.

Tools/Localization/inventory.json stores Chinese source strings and stable keys. translations.json maps each numeric ID to English, Russian, Japanese and French in that order. def-entries.json maps definition types and injection paths to source IDs.

Edit the source catalog and all four translations together, preserving placeholders, rich-text tags and game grammar tokens. Regenerate and verify:

~~~powershell
python Tools/Localization/generate.py
python Tools/Localization/verify.py
~~~

Generated XML is committed. New runtime strings need an inventory entry and an NmcText.Get call; new definition fields need an entry in def-entries.json. Custom translated fields require MustTranslate. Do not resolve translated text into static caches that survive a language reload.

Each load-folder branch uses a different XML filename. RimWorld gives later load folders priority and can skip an earlier file with the same relative path. Keep NMC_1_6.xml and NMC_Royalty.xml distinct.

Static checks validate coverage, placeholders, tags and generated output. New definition paths must also be tested with RimWorld's native loader. They cannot be proven valid by XML parsing alone.
