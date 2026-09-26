<div dir="ltr" align=center>

[**Usage**](Usage.md) / [**Keybinds**](Keybinds.md) / [**BLOC Format**](BLOC_FORMAT.md) / [**FAQ**](FAQ.md) / [**How It Works**](HowItWorks.md) / [**MCP**](MCP.md)
</div>

# BLOC (Binary Localization Container) Format

## Overview

BLOC is a compact binary format designed specifically for localization data. It optimizes for:

- **Small file size** - 50-70% smaller than JSON
- **Fast loading** - O(1) lookups, minimal parsing overhead
- **String deduplication** - Automatic elimination of duplicate text
- **Memory efficiency** - Direct binary representation
- **Optional compression** - Built-in Deflate compression support

## File Structure

### Uncompressed Format

```
┌─────────────────┐
│     Header      │ 32 bytes
├─────────────────┤
│   Entry Table   │ Variable (8+ bytes per entry)
├─────────────────┤
│   String Pool   │ Variable (deduplicated UTF-8)
├─────────────────┤
│  CRC32 Footer   │ 4 bytes
└─────────────────┘
```

### Compressed Format

```
┌─────────────────┐
│     Header      │ 32 bytes (FlagCompressed = 1)
├─────────────────┤
│  CompressedData │ Variable (Deflate stream)
└─────────────────┘
```

**Note:** In compressed files, the `StringPoolOffset` field in the header stores the uncompressed size instead of an offset.

---

## Header (32 bytes)

| Offset | Size | Field            | Description                                              |
| ------ | ---- | ---------------- | -------------------------------------------------------- |
| 0x00   | 4    | Magic            | "BLOC" (0x42, 0x4C, 0x4F, 0x43)                          |
| 0x04   | 2    | Version          | Format version (currently 1)                             |
| 0x06   | 2    | Flags            | Bit flags (see below)                                    |
| 0x08   | 12   | LanguageCode     | Language code (null-padded ASCII, e.g., "en\0\0...")     |
| 0x14   | 4    | EntryCount       | Number of translation entries                            |
| 0x18   | 4    | StringCount      | Number of unique strings in pool                         |
| 0x1C   | 4    | StringPoolOffset | Offset to string pool OR uncompressed size if compressed |

### Flags

| Bit  | Flag       | Description                   |
| ---- | ---------- | ----------------------------- |
| 0    | Compressed | File uses Deflate compression |
| 1-15 | Reserved   | Future use                    |

### Language Code

The 12-byte language code field supports various language code formats:
- Simple codes: "en", "ru", "ja"
- Regional variants: "zh-hans", "zh-hant", "sr-Latn"

Padded with null bytes to 12 bytes.

---

## Compression

BLOC uses **Deflate** compression (same as ZIP/gzip) via `System.IO.Compression.DeflateStream`.

### Smart Compression

The serializer automatically checks if compression actually helps:

```csharp
if (compressed.Length < uncompressedData.Length - 4)
{
    // Use compressed format
}
// Otherwise store uncompressed
```

- Small files (< 1KB) often don't benefit from compression
- Only stores compressed if it saves at least 4 bytes

---

## Entry Table

Contains all translation key-value pairs. Variable size depending on data.

### String Entry (8 bytes)

| Field    | Size    | Description                          |
| -------- | ------- | ------------------------------------ |
| KeyId    | 4 bytes | Index into string pool for the key   |
| StringId | 4 bytes | Index into string pool for the value |

### Array Entry (8+ bytes)

| Field       | Size      | Description                        |
| ----------- | --------- | ---------------------------------- |
| KeyId       | 4 bytes   | Index into string pool for the key |
| ArrayHeader | 4 bytes   | High bit set + count (0x80000000   | count) |
| ItemIds     | 4*N bytes | Indices for each array element     |

**Array Header Format:**

- Bit 31: Always 1 (indicates array)
- Bits 0-30: Array item count (max ~2 billion items)

---

## String Pool

Deduplicated storage for all strings used in the file.

### Encoding

Each string uses **VarInt** (variable-length integer) encoding for the length prefix:

```
[VarInt: Length] [UTF-8 Data]
```

### VarInt Encoding

7 bits per byte, continuation flag:

| Value Range      | Bytes Used | Encoding                     |
| ---------------- | ---------- | ---------------------------- |
| 0-127            | 1          | `0xxxxxxx`                   |
| 128-16,383       | 2          | `1xxxxxxx 0xxxxxxx`          |
| 16,384-2,097,151 | 3          | `1xxxxxxx 1xxxxxxx 0xxxxxxx` |

### Deduplication

The string pool eliminates duplicates:

```json
{
  "greeting": "Hello",
  "farewell": "Hello",
  "welcome": "Hello World"
}
```

String pool contents:

1. "greeting"
2. "farewell"
3. "welcome"
4. "Hello" ← Stored once, referenced 3 times
5. "Hello World"

**Savings:** "Hello" (5 bytes) stored once instead of 3 times = 10 bytes saved.

---

## CRC32 Checksum

Uncompressed files include a 4-byte CRC32 footer for data integrity verification.

The CRC32 algorithm uses polynomial `0xEDB88320` with standard initialization.

Compressed files do not have a separate CRC32 footer (Deflate includes its own checksum).

---

## Complete Example

### Input Data (JSON representation)

```json
{
  "ui.play": "Play",
  "ui.quit": "Quit",
  "difficulty.options": ["Easy", "Medium", "Hard"],
  "ui.back": "Back"
}
```

### Uncompressed BLOC (120 bytes)

```
[Header: 32 bytes]
  Magic: "BLOC"
  Version: 1
  Flags: 0 (uncompressed)
  LanguageCode: "en\0\0..." (12 bytes)
  EntryCount: 4
  StringCount: 9
  StringPoolOffset: 64

[Entry Table: 40 bytes]
  ui.play → "Play"
  ui.quit → "Quit"
  difficulty.options → ["Easy", "Medium", "Hard"]
  ui.back → "Back"

[String Pool: 44 bytes]
  [0-8]: 9 unique strings

[Footer: 4 bytes]
  CRC32
```

### Compressed BLOC (~65 bytes)

```
[Header: 32 bytes]
  Magic: "BLOC"
  Version: 1
  Flags: 1 (compressed)
  StringPoolOffset: 120 (uncompressed size)
  ...

[CompressedData: ~33 bytes]
  Deflate stream of the 120 bytes
```

---

## Size Comparison

### Without Compression

| Format           | Size       | Notes              |
| ---------------- | ---------- | ------------------ |
| JSON (formatted) | 100%       | Human-readable     |
| JSON (minified)  | 70%        | No whitespace      |
| **BLOC**         | **40-50%** | **Binary + dedup** |
| MessagePack      | 55-65%     | General binary     |

### With Compression

| Format             | Size       | Notes                       |
| ------------------ | ---------- | --------------------------- |
| JSON + gzip        | 25-35%     | Requires decompression      |
| **BLOC + Deflate** | **15-25%** | **Built-in, auto-detected** |
| ZIP archive        | 20-30%     | Multiple files              |

**Real-world example:**

- 280KB JSON
- 170KB BLOC (uncompressed)
- **85KB BLOC (compressed)** ← 70% smaller than JSON!

---

## Technical Specifications

### Limits

| Limit               | Value                 |
| ------------------- | --------------------- |
| Max file size       | 4 GB (32-bit offsets) |
| Max entries         | 4,294,967,295         |
| Max strings in pool | 4,294,967,295         |
| Max string length   | 4,294,967,295 bytes   |
| Max array items     | 2,147,483,647         |
| Min file size       | 36 bytes              |

### Compression

- **Algorithm:** Deflate
- **Implementation:** `System.IO.Compression.DeflateStream`
- **Checksum:** CRC32 for uncompressed files

### Endianness

Little-endian (standard for Unity/.NET platforms).

---

## Performance

### File Sizes

| Content Type         | JSON  | BLOC  | BLOC+Compress | Savings |
| -------------------- | ----- | ----- | ------------- | ------- |
| UI text (repetitive) | 100KB | 45KB  | **18KB**      | **82%** |
| Long descriptions    | 500KB | 300KB | **120KB**     | **76%** |
| Mixed game content   | 1MB   | 550KB | **220KB**     | **78%** |

### Loading Speed

| Operation      | Time Complexity | Notes                   |
| -------------- | --------------- | ----------------------- |
| Decompression  | O(N)            | Only if compressed      |
| Deserialize    | O(N)            | Single pass             |
| Build lookup   | O(N)            | Creates dictionary once |
| Runtime lookup | O(1)            | Dictionary access       |

### Memory Usage

| Phase           | Memory                    |
| --------------- | ------------------------- |
| Serialization   | ~2x file size (temporary) |
| Deserialization | ~1.5x file size (runtime) |
| Runtime lookup  | O(1) additional           |

---

## Validation

Minimum valid BLOC file: **36 bytes** (32-byte header + 4-byte CRC32)

Validation checks:
1. File size >= 36 bytes
2. Magic number matches "BLOC"
3. Version is exactly 1
4. CRC32 checksum matches (uncompressed files only)
