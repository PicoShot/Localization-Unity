#pragma author PicoShot
#pragma description PicoShot Localization BLOC v3 locale file (.bloc)
#pragma magic [ 42 4C 4F 43 03 00 ] @ 0x00
#pragma endian little
#pragma pattern_limit 2000000

import std.core;
import std.mem;
import std.string;
#ifndef BLOC_NO_INFLATE
import hex.dec;
#endif

enum TextEncoding : u8 {
    Utf8    = 0,
    Utf16   = 1,
    Window8 = 2
};

enum Codec : u8 {
    None    = 0,
    Deflate = 1
};

fn utf8_from_code_point(u32 cp) {
    if (cp < 0x80)
        return str(char(cp));
    if (cp < 0x800)
        return str(char(0xC0 | (cp >> 6))) + char(0x80 | (cp & 0x3F));
    if (cp < 0x10000)
        return str(char(0xE0 | (cp >> 12))) + char(0x80 | ((cp >> 6) & 0x3F)) + char(0x80 | (cp & 0x3F));
    return str(char(0xF0 | (cp >> 18))) + char(0x80 | ((cp >> 12) & 0x3F)) + char(0x80 | ((cp >> 6) & 0x3F)) + char(0x80 | (cp & 0x3F));
};

fn decode_text(ref auto bytes, TextEncoding encoding, u16 window_base) {
    u32 count = sizeof(bytes);
    str result = "";
    u32 i = 0;

    if (encoding == TextEncoding::Utf16) {
        while (i + 1 < count) {
            u32 c = bytes[i] | (u32(bytes[i + 1]) << 8);
            i = i + 2;
            if (c >= 0xD800 && c <= 0xDBFF && i + 1 < count) {
                u32 low = bytes[i] | (u32(bytes[i + 1]) << 8);
                if (low >= 0xDC00 && low <= 0xDFFF) {
                    c = 0x10000 + ((c - 0xD800) << 10) + (low - 0xDC00);
                    i = i + 2;
                }
            }
            result = result + utf8_from_code_point(c);
        }
        return result;
    }

    while (i < count) {
        u8 b = bytes[i];
        if (encoding == TextEncoding::Window8 && b >= 0x81) {
            result = result + utf8_from_code_point(window_base + (b - 0x81));
        } else if (encoding == TextEncoding::Window8 && b == 0x80 && i + 1 < count) {
            u8 lead = bytes[i + 1];
            u32 length = lead >= 0xF0 ? 4 : (lead >= 0xE0 ? 3 : 2);
            for (u32 j = 0, j < length && i + 1 + j < count, j = j + 1)
                result = result + char(bytes[i + 1 + j]);
            i = i + length;
        } else {
            result = result + char(b);
        }
        i = i + 1;
    }
    return result;
};

fn fmt_hex32(u32 v) { return std::format("0x{:08X}", v); };
fn fmt_hex64(u64 v) { return std::format("0x{:016X}", v); };

fn fmt_window(u16 v) {
    if (v == 0)
        return "0 (unused)";
    return std::format("U+{:04X}..U+{:04X} -> bytes 0x81..0xFF", v, v + 126);
};

fn fmt_varuint(ref auto v) { return std::format("{}", v.value); };

fn fmt_section_entry(ref auto e) {
    return std::format("{} {} stored={} raw={}", e.tag, e.codec, e.stored_size, e.raw_size);
};

fn fmt_section_body(ref auto s) {
    return std::format("{} ({} bytes)", s.tag, sizeof(s.stored));
};

fn fmt_string_ref(ref auto r) {
    return std::format("#{}{}", r.string_id, r.is_new ? " (new)" : "");
};

fn fmt_entry(ref auto e) {
    if (e.type == "string")
        return std::format("string #{}{}", e.string_id, e.is_new ? " (new)" : "");
    if (e.type == "array")
        return std::format("array[{}]", e.payload);
    return e.type;
};

fn fmt_text(ref auto t) {
    return std::format("\"{}\"", t.value);
};

fn fmt_key(ref auto k) {
    return std::format("{} = {}", k.name, k.value);
};


bitfield Flags {
    required : 16;
    optional : 16;
};

struct Header {
    char         magic[4];
    u16          version;
    u16          header_size;
    Flags        flags;
    char         language[16];
    u32          entry_count;
    u64          key_set_id [[format("fmt_hex64"), comment("XXH64 of the KEYS block")]];
    TextEncoding text_encoding;
    u8           section_count;
    u16          window_base [[format("fmt_window"), comment("Window8 only: first code point of the 127-char window")]];
    u32          header_check [[format("fmt_hex32"), comment("XXH32 of header[0..0x2C] + extra header + directory")]];
    if (header_size > 48)
        u8 extra_header[header_size - 48];
};

struct SectionEntry {
    char  tag[4];
    Codec codec;
    u8    reserved[3] [[hidden]];
    u32   stored_size;
    u32   raw_size;
    u32   checksum [[format("fmt_hex32"), comment("XXH32 of the stored bytes")]];
} [[format("fmt_section_entry")]];

Header       header    @ 0x00;
SectionEntry directory[header.section_count] @ header.header_size;

struct SectionBody {
    u32 index = std::core::array_index();
    str tag = directory[index].tag;
    u8  stored[directory[index].stored_size];
} [[format("fmt_section_body")]];

SectionBody sections[header.section_count] @ header.header_size + header.section_count * 20;

u32 g_string_count = 0;
str g_prev_key = "";

struct VarUInt {
    u8  b0;
    u32 value = b0 & 0x7F;
    if (b0 & 0x80) {
        u8 b1;
        value = value | (u32(b1 & 0x7F) << 7);
        if (b1 & 0x80) {
            u8 b2;
            value = value | (u32(b2 & 0x7F) << 14);
            if (b2 & 0x80) {
                u8 b3;
                value = value | (u32(b3 & 0x7F) << 21);
                if (b3 & 0x80) {
                    u8 b4;
                    value = value | (u32(b4 & 0x0F) << 28);
                }
            }
        }
    }
} [[sealed, format("fmt_varuint")]];

fn resolve_ref(u32 reference) {
    if (reference == 0) {
        g_string_count = g_string_count + 1;
        return g_string_count - 1;
    }
    return reference - 1;
};

struct StringRef {
    VarUInt raw [[comment("0 = next new string, n = reuse string n-1")]];
    u32 string_id = resolve_ref(raw.value);
    bool is_new = raw.value == 0;
} [[sealed, format("fmt_string_ref")]];

enum PluralCategory : u8 { Zero, One, Two, Few, Many, Other };

bitfield PluralMask {
    zero  : 1;
    one   : 1;
    two   : 1;
    few   : 1;
    many  : 1;
    other : 1;
    padding : 2;
};

struct Entry {
    VarUInt tag [[comment("low 2 bits: kind (0 string, 1 array, 2 extended); rest: payload")]];
    u8  kind    = tag.value & 3;
    u32 payload = tag.value >> 2;

    if (kind == 0) {
        str type = "string";
        u32 string_id = resolve_ref(payload);
        bool is_new = payload == 0;
    } else if (kind == 1) {
        str type = "array";
        StringRef items[payload];
    } else if (kind == 2 && payload == 0) {
        str type = "missing";
    } else if (kind == 2 && payload == 1) {
        str type = "plural";
        PluralMask forms [[comment("bit per category present: zero one two few many other")]];
        if (forms.zero)  StringRef zero;
        if (forms.one)   StringRef one;
        if (forms.two)   StringRef two;
        if (forms.few)   StringRef few;
        if (forms.many)  StringRef many;
        if (forms.other) StringRef other;
    } else {
        str type = "INVALID";
        std::error(std::format("Unknown entry kind {} payload {} at 0x{:X}", kind, payload, addressof(tag)));
    }
} [[format("fmt_entry")]];

struct Text {
    u32 id = std::core::array_index();
    u8  bytes[parent.lengths[id].value];
    str value = decode_text(bytes, header.text_encoding, header.window_base);
} [[format("fmt_text")]];

fn quoted_text(ref auto text, u32 string_id) {
    return std::format("\"{}\"", text[string_id].value);
};

fn entry_value(ref auto entry, ref auto text) {
    if (entry.type == "string")
        return quoted_text(text, entry.string_id);

    if (entry.type == "array") {
        str result = "[";
        for (u32 i = 0, i < entry.payload, i = i + 1) {
            if (i > 0)
                result = result + ", ";
            result = result + quoted_text(text, entry.items[i].string_id);
        }
        return result + "]";
    }

    if (entry.type == "plural") {
        str result = "{";
        if (entry.forms.zero)  result = result + "zero: "  + quoted_text(text, entry.zero.string_id)  + ", ";
        if (entry.forms.one)   result = result + "one: "   + quoted_text(text, entry.one.string_id)   + ", ";
        if (entry.forms.two)   result = result + "two: "   + quoted_text(text, entry.two.string_id)   + ", ";
        if (entry.forms.few)   result = result + "few: "   + quoted_text(text, entry.few.string_id)   + ", ";
        if (entry.forms.many)  result = result + "many: "  + quoted_text(text, entry.many.string_id)  + ", ";
        return result + "other: " + quoted_text(text, entry.other.string_id) + "}";
    }

    return "<" + entry.type + ">";
};

struct Key {
    u32 index = std::core::array_index();
    VarUInt shared [[comment("bytes shared with the previous key")]];
    VarUInt suffix_length;
    char    suffix[suffix_length.value];
    str name = std::string::substr(g_prev_key, 0, shared.value) + suffix;
    g_prev_key = name;
    str value = entry_value(parent.entries[index], parent.text);
    std::core::set_display_name(parent.entries[index], name);
    std::core::set_pattern_comment(parent.entries[index], value);
} [[format("fmt_key")]];

struct DataBlock {
    Entry   entries[header.entry_count];
    VarUInt lengths[g_string_count] [[comment("byte length of each string in TEXT")]];
    Text    text[g_string_count];
    Key     keys[header.entry_count] [[comment("sorted, front-coded UTF-8 key names")]];
};

fn find_data_index() {
    for (u32 i = 0, i < header.section_count, i = i + 1) {
        if (directory[i].tag == "DATA")
            return i;
    }
    return -1;
};

u32 g_data_index = find_data_index();

std::mem::Section g_data_section = 0;
u64 g_data_address = addressof(sections[g_data_index].stored);

if (directory[g_data_index].codec == Codec::Deflate) {
#ifndef BLOC_NO_INFLATE
    g_data_section = std::mem::create_section("DATA (inflated)");
    hex::dec::zlib_decompress(sections[g_data_index].stored, g_data_section, -15);
    if (std::mem::get_section_size(g_data_section) != directory[g_data_index].raw_size)
        std::error(std::format("Inflated DATA is {} bytes, expected {}", std::mem::get_section_size(g_data_section), directory[g_data_index].raw_size));
    g_data_address = 0;
#endif
#ifdef BLOC_NO_INFLATE
    std::error("DATA is compressed and inflating is disabled (BLOC_NO_INFLATE)");
#endif
}

DataBlock data @ g_data_address in g_data_section;
