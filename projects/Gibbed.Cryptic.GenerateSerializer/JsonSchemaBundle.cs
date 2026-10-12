/* Copyright (c) 2026 Kurgh (kurgh@kurgh.com)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Gibbed.Cryptic.FileFormats;

namespace Gibbed.Cryptic.GenerateSerializer
{
    // STOdex schema-tree JSON is an input representation, not native memory offsets
    // reinterpreted as BIN positions. The established token decoder stays authoritative.
    public sealed class JsonSchemaBundle
    {
        public string GameBuild { get; }
        public string SchemaId { get; }
        public ParseLoader Parses { get; }
        public EnumLoader Enums { get; }
        private int _Nodes;

        public JsonSchemaBundle(string path)
        {
            using var input = File.OpenRead(path);
            if (input.Length > 128 * 1024 * 1024) throw new FormatException("Schema JSON exceeds 128 MiB");
            using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 256 });
            var root = document.RootElement;
            if (root.GetProperty("format").GetString() != "stodex-schema-tree" || root.GetProperty("version").GetInt32() != 1
                || root.GetProperty("representation").GetString() != "bin-schema-metadata")
                throw new FormatException("Unsupported BIN schema JSON representation");
            this.GameBuild = Identity(root.GetProperty("game_build").GetString());
            this.SchemaId = Identity(root.GetProperty("schema_id").GetString());
            var parses = new Dictionary<string, ParseSchemaFile>();
            var enums = new Dictionary<string, ParseEnumFile>();
            foreach (var row in root.GetProperty("parses").EnumerateObject())
            {
                var xml = Element(row.Value, 0);
                if (xml.Name != "parse") throw new FormatException("Expected parse root");
                using var reader = new StringReader(xml.ToString(SaveOptions.DisableFormatting));
                var parse = ParseSchemaFile.LoadDocument(new XPathDocument(reader), row.Name);
                if (string.IsNullOrEmpty(parse.Name)) throw new FormatException("Parse has no name");
                parses.Add(parse.Name, parse);
                if (parses.Count > 20000) throw new FormatException("Too many parses");
            }
            foreach (var row in root.GetProperty("enums").EnumerateObject())
            {
                var xml = Element(row.Value, 0);
                if (xml.Name != "enum") throw new FormatException("Expected enum root");
                using var reader = new StringReader(xml.ToString(SaveOptions.DisableFormatting));
                var enumeration = ParseEnumFile.LoadDocument(new XPathDocument(reader), row.Name);
                if (string.IsNullOrEmpty(enumeration.Name)) throw new FormatException("Enum has no name");
                enums.Add(enumeration.Name, enumeration);
                if (enums.Count > 20000) throw new FormatException("Too many enums");
            }
            if (parses.Count == 0 || enums.Count == 0) throw new FormatException("Schema JSON has no parses or enums");
            this.Parses = new ParseLoader(parses);
            this.Enums = new EnumLoader(enums);
        }

        private static string Identity(string value)
        {
            if (value == null || value.Length > 200 || value == "." || value == ".." || !Regex.IsMatch(value, "^[A-Za-z0-9_.-]+$"))
                throw new FormatException("Invalid schema identity");
            return value;
        }

        private static string Text(JsonElement value)
        {
            var text = value.GetString();
            if (text == null || text.Length > 1024 * 1024) throw new FormatException("Invalid schema text");
            return text;
        }

        private XElement Element(JsonElement node, int depth)
        {
            if (depth > 128 || ++this._Nodes > 2000000) throw new FormatException("Schema tree exceeds limits");
            var tag = XmlConvert.VerifyNCName(Text(node.GetProperty("tag")));
            var result = new XElement(tag);
            foreach (var attribute in node.GetProperty("attributes").EnumerateObject())
                result.Add(new XAttribute(XmlConvert.VerifyNCName(attribute.Name), Text(attribute.Value)));
            if (node.TryGetProperty("text", out var text)) result.Add(Text(text));
            if (node.TryGetProperty("children", out var children))
                foreach (var child in children.EnumerateArray()) result.Add(Element(child, depth + 1));
            return result;
        }
    }
}
