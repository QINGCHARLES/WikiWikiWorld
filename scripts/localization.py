"""Check and update the shared UI text resources (src/WikiWikiWorld.Web/Resources/SharedResource*.resx).

Keys are the English text. The script finds every key the code uses and reports the ones a translation file
lacks, because a missing key silently falls back to English on Arabic and Japanese pages.

  python scripts/localization.py check            # list missing and unused keys; exit 1 if any are missing
  python scripts/localization.py merge batch.json # add or replace translations

A merge file maps each key to its translations, plus an English value for synthetic keys (plural forms):

  {"Log In": {"ar": "...", "ja": "..."},
   "TimeAgo.Minute|one": {"en": "{0} minute", "ar": "...", "ja": "..."}}

A null value removes that key from that culture's file.

Keys found in code:
  L["..."] / HL["..."] in Razor, SharedResource.Text/Format/FormatHtml("...") in C#, SharedResource.Plural("Stem", n)
  (needs Stem|one and Stem|other in English, every Arabic category, and Stem|other in Japanese), and
  data-l10n keys listed in `ScriptKeys` comments: // l10n: "Key one", "Key two"
"""

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

Root = Path(__file__).resolve().parent.parent
Web = Root / "src" / "WikiWikiWorld.Web"
Resources = Web / "Resources"
Files = {"en": Resources / "SharedResource.resx", "ar": Resources / "SharedResource.ar.resx", "ja": Resources / "SharedResource.ja.resx"}
Cultures = ("ar", "ja")
ArabicCategories = ("zero", "one", "two", "few", "many", "other")

StringLiteral = r'"((?:[^"\\]|\\.)*)"'
Patterns = [
	re.compile(r'\bH?L\[\s*' + StringLiteral),
	re.compile(r'SharedResource\.(?:Text|Format|FormatHtml)\(\s*' + StringLiteral),
]
PluralPattern = re.compile(r'SharedResource\.Plural(?:Pattern|Forms)?\(\s*' + StringLiteral)
Synthetic = re.compile(r'[A-Z][A-Za-z]*(\.[A-Z][A-Za-z]*)+')
ScriptKeysPattern = re.compile(r'l10n:\s*(.+)$', re.MULTILINE)

Header = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
"""


def Unescape(Text):
	"""Turns a C# regular string literal body into its value."""
	return re.sub(r'\\(.)', lambda M: {"n": "\n", "t": "\t"}.get(M.group(1), M.group(1)), Text)


def UsedKeys():
	"""Returns (plain keys, plural stems) used anywhere under the web project."""
	Keys, Stems = set(), set()
	for Path_ in list(Web.rglob("*.cshtml")) + list(Web.rglob("*.cs")):
		if any(Part in ("bin", "obj") for Part in Path_.parts):
			continue
		Text = Path_.read_text(encoding="utf-8-sig")
		for Pattern in Patterns:
			Keys.update(Unescape(M.group(1)) for M in Pattern.finditer(Text))
		Stems.update(Unescape(M.group(1)) for M in PluralPattern.finditer(Text))
		for M in ScriptKeysPattern.finditer(Text):
			Keys.update(Unescape(K) for K in re.findall(StringLiteral, M.group(1)))
	return Keys, Stems


def Load(Culture):
	"""Reads a resx file into an ordered dict (empty when the file does not exist)."""
	File = Files[Culture]
	if not File.exists():
		return {}
	return {Data.get("name"): (Data.findtext("value") or "") for Data in ET.parse(File).getroot().iter("data")}


def Save(Culture, Entries):
	"""Writes a resx file with entries sorted by key, CRLF line endings, UTF-8 with BOM."""
	Lines = [Header]
	for Key in sorted(Entries, key=str.casefold):
		Name = Key.replace("&", "&amp;").replace('"', "&quot;").replace("<", "&lt;").replace(">", "&gt;")
		Value = Entries[Key].replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
		Lines.append(f'  <data name="{Name}" xml:space="preserve"><value>{Value}</value></data>\n')
	Lines.append("</root>\n")
	Files[Culture].parent.mkdir(parents=True, exist_ok=True)
	Files[Culture].write_bytes("".join(Lines).replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8-sig"))


def Check():
	"""Prints missing and unused keys per file; returns the process exit code."""
	Keys, Stems = UsedKeys()
	Required = {"en": set(), "ar": set(Keys), "ja": set(Keys)}
	# Synthetic keys (Area.Name, not English text) need an English value in the neutral file.
	Required["en"].update(K for K in Keys if Synthetic.fullmatch(K))
	for Stem in Stems:
		Required["en"].update({f"{Stem}|one", f"{Stem}|other"})
		Required["ar"].update(f"{Stem}|{C}" for C in ArabicCategories)
		Required["ja"].add(f"{Stem}|other")
	Missing = 0
	for Culture, Needed in Required.items():
		Have = Load(Culture)
		Absent = sorted(Needed - Have.keys(), key=str.casefold)
		Extra = sorted(Have.keys() - Needed, key=str.casefold)
		# Keys ending in .Suffix may be empty on purpose (Japanese drops the "article" after "Edit", for instance).
		Empty = sorted(K for K in Needed & Have.keys() if not Have[K].strip() and not K.endswith(".Suffix"))
		Missing += len(Absent) + len(Empty)
		print(f"{Files[Culture].name}: {len(Have)} entries, {len(Absent)} missing, {len(Empty)} empty, {len(Extra)} unused")
		for Key in Absent:
			print(f"  missing: {Key!r}")
		for Key in Empty:
			print(f"  empty:   {Key!r}")
		for Key in Extra:
			print(f"  unused:  {Key!r}")
	# The resource compiler treats names case-insensitively and keeps only one of "Log In" and "Log in", so such
	# pairs silently lose a translation. Give one of them a synthetic key instead.
	ByFold = {}
	for Key in Keys | Required["ar"]:
		ByFold.setdefault(Key.casefold(), set()).add(Key)
	Clashes = sorted(sorted(Group) for Group in ByFold.values() if len(Group) > 1)
	for Group in Clashes:
		print(f"  case clash: {Group}")
	print(f"{len(Keys)} keys and {len(Stems)} plural stems used in code")
	return 1 if Missing or Clashes else 0


def Merge(Path_):
	"""Adds or replaces translations from a JSON file."""
	Batch = json.loads(Path(Path_).read_text(encoding="utf-8"))
	for Culture in ("en",) + Cultures:
		Entries = Load(Culture)
		Changed = False
		for Key, Values in Batch.items():
			if Culture not in Values:
				continue
			if Values[Culture] is None:
				Entries.pop(Key, None)
			else:
				Entries[Key] = Values[Culture]
			Changed = True
		if Changed:
			Save(Culture, Entries)
	print(f"merged {len(Batch)} keys")
	return 0


if __name__ == "__main__":
	if len(sys.argv) >= 2 and sys.argv[1] == "check":
		sys.exit(Check())
	if len(sys.argv) == 3 and sys.argv[1] == "merge":
		sys.exit(Merge(sys.argv[2]))
	print(__doc__)
	sys.exit(2)
