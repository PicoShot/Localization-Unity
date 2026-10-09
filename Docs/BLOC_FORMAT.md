<div dir="ltr" align=center>

[**Usage**](Usage.md) / [**Keybinds**](Keybinds.md) / [**BLOC Format**](BLOC_FORMAT.md) / [**FAQ**](FAQ.md) / [**How It Works**](HowItWorks.md) / [**MCP**](MCP.md)
</div>

# BLOC (Binary Localization Container) Format

BLOC is the binary file format of PicoShot Localization: one `.bloc` file per language, in the `Locales/` folder.
This page specifies **version 3**, which the editor and the MCP server write. Versions 1 and 2 can still be read,
and the Language Editor offers to upgrade them (see [Previous Versions](#previous-versions)).

## Overview

BLOC v3 is built around what localization data actually looks like: thousands of short texts, keys that share
long prefixes (`settings.audio.music`), the same key list in every language, and many scripts (Latin, Cyrillic,
Arabic, CJK, ...).

| Goal | How v3 gets there |
| --- | --- |
| Small files | Front-coded keys, 1-byte entry references, a per-file text encoding (UTF-8, UTF-16 or a 1-byte script window), Deflate |
| Fast loading | No per-entry objects: values stay encoded until first read; key lookup index built in O(n) or shared between languages |
| Fast lookups | Open-addressing hash table on the 64-bit key hash: O(1) |
| Safety | Header checksum, per-section checksum, exact decompressed size, bounds-checked parser, strict deep validation |
| Stability | Deterministic output: the same data always produces the same bytes |
| Future-proof | Chunked sections, must-understand flags and codec ids: new features do not need a new version |

Measured on a project with 19 languages × 263 keys (`.NET Deflate, Optimal`):

| | v2 | v3 |
| --- | --- | --- |
| Total size, compressed | 162,828 bytes | **122,487 bytes (−24.8%)** |
| Total size, uncompressed | 332,952 bytes | **223,658 bytes (−32.8%)** |
| Load one language, 263 keys | 151 µs, 77 KB allocated | **19 µs, 25 KB** |
| Load one language, 10,520 keys | 3.4 ms, 2.6 MB allocated | **0.15 ms, 0.65 MB** |
| Look up 10,520 keys | 499 µs | **15 µs** |

*Timings are from .NET on a desktop CPU and only show the relative difference; Unity Mono/IL2CPP numbers differ.*

---

## File Layout

All numbers are little-endian.

```
┌────────────────────────────┐
│ Header           48 bytes  │  checked by HeaderCheck
├────────────────────────────┤
│ Section directory          │  20 bytes per section
├────────────────────────────┤
│ Section 0 (DATA)           │  sections follow each other in directory order,
│ Section 1 ... (optional)   │  with no gaps, up to the end of the file
└────────────────────────────┘
```

### Header (48 bytes)

| Offset | Size | Field | Description |
| --- | --- | --- | --- |
| 0x00 | 4 | Magic | `BLOC` (0x42 0x4C 0x4F 0x43) |
| 0x04 | 2 | Version | 3 |
| 0x06 | 2 | HeaderSize | Size of the header, ≥ 48. The directory starts here, so a later 3.x can grow the header and older readers skip the extra bytes |
| 0x08 | 4 | Flags | Low 16 bits: *must understand*; a reader refuses a file with a set bit it does not know. High 16 bits: optional, ignored when unknown. None are defined yet |
| 0x0C | 16 | LanguageTag | Language code, printable ASCII, padded with zeros (fits `zh-Hant-HK`, `ca-ES-valencia`) |
| 0x1C | 4 | EntryCount | Number of keys |
| 0x20 | 8 | KeySetId | xxHash64 of the KEYS block (see [Shared Key Index](#shared-key-index)) |
| 0x28 | 1 | TextEncoding | 0 = UTF-8, 1 = UTF-16LE, 2 = Window-8 |
| 0x29 | 1 | SectionCount | 1-16 |
| 0x2A | 2 | WindowBase | First character of the Window-8 range; 0 for the other encodings |
| 0x2C | 4 | HeaderCheck | xxHash32 of header bytes `0x00-0x2B` followed by bytes `0x30` up to the end of the directory |

### Section Directory (20 bytes per section)

| Offset | Size | Field | Description |
| --- | --- | --- | --- |
| 0 | 4 | Tag | Four ASCII letters. An **uppercase** first letter means the section is required: a reader that does not know it refuses the file. Lowercase: optional, skipped when unknown (the PNG chunk rule) |
| 4 | 1 | Codec | 0 = stored, 1 = Deflate (raw, RFC 1951). Other values are reserved |
| 5 | 1 | Reserved | 0 |
| 6 | 2 | Reserved | 0 |
| 8 | 4 | StoredSize | Bytes in the file |
| 12 | 4 | RawSize | Bytes after decompression; must equal StoredSize when stored |
| 16 | 4 | Checksum | xxHash32 of the stored bytes |

Version 3 defines one section, **`DATA`**, which must appear exactly once.

---

## The DATA Section

After decompression, DATA holds four blocks back to back. None of them has an offset or a size field:
each boundary follows from decoding the block before it.

```
ENTRIES │ LENGTHS │ TEXT │ KEYS
```

Entries and keys are in the same order: key names sorted by ordinal (UTF-16 code unit) comparison.
Entry `i` is the value of key `i`.

### Varints

Numbers are unsigned LEB128: 7 bits per byte, low bits first, the high bit set on every byte except the last.
At most 5 bytes, value below 2³².

### ENTRIES

One varint **tag** per entry. The low 2 bits are the kind, the rest is the payload:

| Kind | Meaning | Payload | Followed by |
| --- | --- | --- | --- |
| 0 | String | String reference | — |
| 1 | Array | Item count | One string reference (varint) per item |
| 2 | Extended | 0 = **Missing** | — |
| 2 | Extended | 1 = **Plural** | One byte category mask, then one string reference per set bit |
| 3 | Reserved | | A reader refuses it |

A **string reference** `r` is `0` for "the next new string" and `n + 1` to reuse string `n`.
Strings are numbered in order of first use, so the common case (a text not seen before) is the single byte
`0x00` and a reused text costs one or two bytes. Arrays and plurals use the same references, without the
2-bit shift.

**Missing** marks a key that exists but has no translation in this language: lookups fall back to the default
language. This differs from an empty string, which is shown as empty.

**Plural** entries hold one text per CLDR category. Mask bits: 0 zero, 1 one, 2 two, 3 few, 4 many, 5 other.
Bit 5 (other) is required, bits 6-7 must be 0. Texts follow in bit order. The runtime does not have a plural
API yet and uses the *other* form wherever a plain string is expected.

### LENGTHS

One varint per unique string: its size in bytes inside TEXT. The number of unique strings is the number of
"new string" references counted while decoding ENTRIES.

### TEXT

All unique strings, in id order, with no separators, in the file's `TextEncoding`:

| Encoding | Bytes | Chosen for |
| --- | --- | --- |
| **UTF-8** (0) | Standard UTF-8 | Mostly-ASCII text |
| **UTF-16LE** (1) | 2 bytes per UTF-16 code unit | CJK: 2 bytes per character instead of 3; decoding is a plain copy |
| **Window-8** (2) | `0x00-0x7F`: ASCII. `0x81-0xFF`: character `WindowBase + (byte − 0x81)`. `0x80`: followed by one character in UTF-8 | Alphabets inside one 127-character block: Cyrillic, Greek, Arabic, Hebrew, Thai, Devanagari, ... and Latin with accents |

The window range `WindowBase … WindowBase + 126` lies inside the BMP and never overlaps the surrogates
(U+D800-U+DFFF). An escaped character is a well-formed UTF-8 sequence of 2-4 bytes, never a surrogate.
With UTF-8 and Window-8, unpaired surrogates in the source text are stored as U+FFFD, as `Encoding.UTF8` does.

The writer picks the encoding with the smallest TEXT, which is also what the runtime keeps in memory.
Ties prefer UTF-8, then Window-8. Measured gain after Deflate, compared with UTF-8: about 16% for Russian,
Ukrainian, Greek and Arabic text; 2-6% for CJK with UTF-16.

### KEYS

Sorted key names in UTF-8, front-coded: each key is

```
varint shared   bytes in common with the previous key (0 for the first key)
varint suffix   number of new bytes
bytes  ...      the new bytes
```

`settings.audio.music` after `settings.audio.master` is `15, 5, "music"`. Keys are not empty and are unique
when compared without case (see below). KEYS ends exactly at the end of DATA.

---

## Key Lookup

### Key Hash

The runtime looks keys up by their 64-bit **FNV-1a** hash over the lower-cased UTF-16 code units
(`Hash64.CreateIgnoreCase`). That makes keys case-insensitive. The writer refuses two keys with the same hash,
so a key can never silently shadow another one.

The loader computes the hashes from KEYS without creating strings: for ASCII keys, it hashes the bytes
directly and resumes from the hash state of the prefix shared with the previous key.

### Index

Hashes go into an open-addressing table (linear probing, Fibonacci hashing, load factor ≤ 0.5):
built in O(n) without sorting, and O(1) per lookup.

### Shared Key Index

`KeySetId` identifies the exact list of keys. The editor writes every key in every language (untranslated
keys are empty strings or Missing entries), so normally all languages have the same KeySetId. When a language
has the same KeySetId as the default language, it reuses the default language's index: changing language
does not hash or index any key.

The id is checked at every load, so a file whose header does not match its keys is refused.

---

## Integrity and Validation

Every reader check throws `InvalidDataException` with a description; the parser never trusts a size or index
before checking it.

| Level | When | Checks |
| --- | --- | --- |
| **Info** | Scanning `Locales/` at startup (`ReadInfo`) | Magic, version, HeaderCheck, required flags, language tag, directory, section sizes adding up exactly to the file length. Reads only the header and directory |
| **Load** | Loading a language | Everything above, plus the DATA checksum *before* decompressing, an exact decompressed size, every entry kind, reference, array count and plural mask, string lengths within TEXT, KeySetId |
| **Deep** | Editor import, MCP, `BlocFormat.Validate` | Everything above, plus strict decoding of every string and key, keys strictly sorted, no two keys with the same hash, checksums of optional sections |

This is separate from **anti-tamper** (SHA256 of each file in the config), which protects against
deliberate changes. The checksums protect against damaged or truncated files.

The MCP `validate` tool adds checks across languages: missing keys, string/array conflicts, array lengths, and
placeholders (`{0}`, `{name}`) or rich-text tags (`<b>`, `</color>`) that differ from the default language.

---

## Compression

DATA is compressed with Deflate when the compression setting is not *Disabled* and it saves at least one byte;
otherwise it is stored. Readers decompress into a buffer of exactly `RawSize` bytes and refuse a stream that
is shorter or longer.

---

## Writer Rules

- Entries are sorted by key, strings are numbered in order of first use, and the encoding choice is
  deterministic: **the same data always gives the same file** (for a given Deflate implementation).
- Two keys with the same hash (normally keys differing only by case) are an error.
- Language tags are 1-16 printable ASCII characters.
- In `LocaleData`, a `null` value writes a Missing entry and a `PluralValue` writes a Plural entry.
  Older versions omit `null` values and refuse plurals.

---

## Example

Five keys, uncompressed (148 bytes):

```json
{ "ui.play": "Play", "ui.quit": "Quit", "ui.back": "Back",
  "difficulty": ["Easy", "Medium", "Hard"], "ui.title": "Play" }
```

```
0000  42 4C 4F 43 03 00 30 00 00 00 00 00 65 6E 00 00   "BLOC", version 3, header 48, flags 0, "en"...
0010  00 00 00 00 00 00 00 00 00 00 00 00 05 00 00 00   ...language tag, EntryCount 5
0020  56 06 6D E6 E6 C0 99 E8 00 01 00 00 86 B0 D8 A3   KeySetId, UTF-8, 1 section, no window, HeaderCheck
0030  44 41 54 41 00 00 00 00 50 00 00 00 50 00 00 00   "DATA", stored, 80 bytes stored, 80 raw
0040  70 25 39 16 0D 00 00 00 00 00 00 14 04 06 04 04   checksum | ENTRIES | LENGTHS
0050  04 04 45 61 73 79 4D 65 64 69 75 6D 48 61 72 64   LENGTHS | TEXT
0060  42 61 63 6B 50 6C 61 79 51 75 69 74 00 0A 64 69   TEXT | KEYS
0070  66 66 69 63 75 6C 74 79 00 07 75 69 2E 62 61 63
0080  6B 03 04 70 6C 61 79 03 04 71 75 69 74 03 05 74
0090  69 74 6C 65
```

| Block | Bytes | Meaning |
| --- | --- | --- |
| ENTRIES | `0D 00 00 00` | `difficulty`: array (kind 1) of 3, three new strings (#0-2) |
| | `00` `00` `00` | `ui.back`, `ui.play`, `ui.quit`: new strings #3, #4, #5 |
| | `14` | `ui.title`: 20 = 5 << 2, reuse string #4 ("Play") |
| LENGTHS | `04 06 04 04 04 04` | Easy, Medium, Hard, Back, Play, Quit |
| TEXT | `EasyMediumHardBackPlayQuit` | 26 bytes |
| KEYS | `00 0A difficulty`, `00 07 ui.back`, `03 04 play`, `03 04 quit`, `03 05 title` | `ui.play` reuses `ui.` from `ui.back` |

The entry table of the five keys takes 8 bytes, against 52 bytes in v2.

---

## Limits

| Limit | Value |
| --- | --- |
| Keys per file | 2²⁸ |
| Section size, stored or decompressed | 100 MB |
| Sections per file | 16 |
| Language tag | 16 ASCII characters |
| Endianness | Little-endian |

---

## Previous Versions

All versions start with `BLOC` and a 2-byte version number; `BlocFormat` reads every version.

| | v1 | v2 | v3 |
| --- | --- | --- | --- |
| Key storage | Full strings in the string pool | Same | Sorted, front-coded |
| References | 4 bytes each | 4 bytes each | Varint, usually 1 byte |
| Text encoding | UTF-8 | UTF-8 | UTF-8, UTF-16 or Window-8 |
| Integrity | CRC32, uncompressed files only | CRC32 of the content | Header check + section checksums, exact sizes |
| Header read at startup | Not verified | Not verified | Verified by HeaderCheck |
| Runtime load | Every string decoded, keys hashed and sorted | Same | Values decoded on first use, O(n) index or shared index |
| Missing / plural values | No | No | Yes |
| Extensible | No | No | Sections and flags |

**Upgrading:** when the Language Editor finds v1 or v2 files it offers to upgrade them; saving from the editor or
MCP always writes v3. `BlocFormat.Upgrade` converts a single file. Runtime builds still read v1/v2 files, more
slowly.
