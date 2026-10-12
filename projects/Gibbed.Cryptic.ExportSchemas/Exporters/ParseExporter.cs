/* Copyright (c) 2021 Rick (rick 'at' gibbed 'dot' us)
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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Gibbed.Cryptic.ExportSchemas.Natives.x64;
using Gibbed.Cryptic.ExportSchemas.Runtime;
using Gibbed.Cryptic.FileFormats;
using static Gibbed.Cryptic.FileFormats.InvariantShorthand;
using Parse = Gibbed.Cryptic.FileFormats.Parse;
using x32 = Gibbed.Cryptic.ExportSchemas.Natives.x32;

namespace Gibbed.Cryptic.ExportSchemas.Exporters
{
    internal class ParseExporter : BaseExporter
    {
        private static readonly string[] _FloatRounding =
        {
            null,
            "HUNDREDTHS",
            "TENTHS",
            "ONES",
            "FIVES",
            "TENS",
        };

        private static readonly string[] _FormatNames =
        {
            null, // 0
            "IP", // 1
            "UNSIGNED", // 2
            "DATESS2000", // 3
            "PERCENT", // 4
            "HSV", // 5
            null, // 6
            "TEXTURE", // 7
            "COLOR", // 8
            "FRIENDLYDATE", // 9
            "FRIENDLYSS2000", // 10
            "KBYTES", // 11
            "FLAGS", // 12
            "INT_WAS_BOOL", // 13
        };

        public ParseExporter(RuntimeBase runtime)
            : base(runtime) { }

        public void Export(string outputBasePath, List<KeyValuePair<string, IntPtr>> enums)
        {
            var runtime = this.Runtime;

            var locators =
                runtime.Is32Bit == false
                    ? new LocateDelegate[] // 64
                    {
                        Locators.x64.ParseTableInfosLocator1.Locate,
                    }
                    : new LocateDelegate[] // 32
                    {
                        Locators.x32.ParseTableInfosLocator1.Locate,
                    };
            if (this.Locate(out var stashPointerPointer, locators) == false)
            {
                Console.WriteLine("Warning: failed to locate parse table.");
                return;
            }

            var stashPointer = runtime.ReadPointer(stashPointerPointer);
            var parses = (runtime.Is32Bit
                ? runtime.ReadStashTable(stashPointer)
                : runtime.ReadPointerKeyedParseTable(stashPointer)).ToList();
            if (parses.Count <= 0)
            {
                return;
            }

            // Local repair: registry keys identify tables, but their first field
            // need not be a PARSETABLE_INFO name. Preserve colliding tables and
            // resolve all subtable links through the same unique name map.
            parses = parses.GroupBy(p => p.Value).Select(g => g.First()).ToList();
            var anchorSpec = Environment.GetEnvironmentVariable("GIBBED_PARSE_ANCHORS");
            if (string.IsNullOrEmpty(anchorSpec) == false)
            {
                foreach (var anchor in anchorSpec.Split(';'))
                {
                    var pair = anchor.Split('=');
                    if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]))
                    {
                        throw new InvalidOperationException("Expected NAME=HEX_RVA parse anchor.");
                    }
                    var rva = Convert.ToInt64(pair[1], 16);
                    if (rva < 0 || rva >= runtime.MainModuleSize)
                    {
                        throw new InvalidOperationException("Parse anchor is outside the module.");
                    }
                    var pointer = new IntPtr(runtime.MainModuleBase.ToInt64() + rva);
                    var index = parses.FindIndex(p => p.Value == pointer);
                    if (index < 0)
                    {
                        throw new InvalidOperationException("Parse anchor is absent from the live registry.");
                    }
                    Console.WriteLine("[anchor] {0} -> {1} at RVA {2:X}", parses[index].Name, pair[0], rva);
                    parses[index] = new StashTableEntry<IntPtr>(pair[0], pointer);
                }
            }
            parses = UniqueNames(parses, runtime.MainModuleBase);

            Directory.CreateDirectory(Path.Combine(outputBasePath, "parses"));

            var failures = new List<string>();

            foreach (var parse in parses.OrderBy(kv => kv.Name))
            {
                var name = parse.Name;
                var pointer = parse.Value;

                Console.WriteLine("[parse] {0}", name);

                var settings = new XmlWriterSettings() { Indent = true };

                var invalidFileNameChars = Path.GetInvalidFileNameChars();
                var safeName = new string(name.Select(c =>
                    invalidFileNameChars.Contains(c) ? '_' : c).ToArray());
                var outputPath = Path.Combine(outputBasePath, "parses", safeName + ".parse.xml");

                /* A single unreadable table should not abandon the export: the
                 * remaining hundreds are still worth having, and naming the one
                 * that failed is what makes it diagnosable. Reading a live
                 * process means an address can always turn out to be stale or
                 * unmapped.
                 */
                try
                {
                    using (var xml = XmlWriter.Create(outputPath, settings))
                    {
                    xml.WriteStartDocument();
                    xml.WriteStartElement("parse");
                    xml.WriteAttributeString("name", name);
                    // Session-specific ASLR address, exported solely to permit
                    // read-only correlation with live resource registrations.
                    xml.WriteAttributeString("runtime_pointer", $"0x{pointer.ToInt64():X}");
                        xml.WriteAttributeString("runtime_address", _($"0x{pointer.ToInt64():X}"));

                        xml.WriteStartElement("table");
                        Export(pointer, xml, parses, enums);
                        xml.WriteEndElement();

                        xml.WriteEndElement();
                        xml.WriteEndDocument();
                    }
                }
                catch (Exception e)
                {
                    failures.Add(name);
                    Console.WriteLine("  Warning: failed to export '{0}': {1}", name, e.Message);

                    /* Don't leave a truncated document behind for the
                     * converters to trip over later.
                     */
                    try
                    {
                        File.Delete(outputPath);
                    }
                    catch (IOException)
                    {
                    }
                }
            }


            if (failures.Count > 0)
            {
                Console.WriteLine(
                    "Warning: {0} of {1} parse tables failed to export: {2}",
                    failures.Count,
                    parses.Count,
                    string.Join(", ", failures));
            }
        }

        internal static List<StashTableEntry<IntPtr>> UniqueNames(
            List<StashTableEntry<IntPtr>> parses, IntPtr moduleBase)
        {
            var invalid = Path.GetInvalidFileNameChars();
            Func<string, string> sanitize = name => new string(
                name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            var collisions = new HashSet<string>(parses
                .GroupBy(p => sanitize(p.Name), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
            var result = parses.Select(p => new StashTableEntry<IntPtr>(
                collisions.Contains(sanitize(p.Name))
                    ? sanitize(p.Name) + "__RVA_" + (p.Value.ToInt64() - moduleBase.ToInt64()).ToString("X")
                    : sanitize(p.Name), p.Value)).ToList();
            if (result.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            {
                throw new InvalidOperationException("Parse output names still collide; refusing to overwrite tables.");
            }
            Console.WriteLine("[parse names] {0} tables; {1} ambiguous name groups", result.Count, collisions.Count);
            return result;
        }

        private void Export(
            IntPtr pointer,
            XmlWriter xml,
            List<StashTableEntry<IntPtr>> parses,
            List<KeyValuePair<string, IntPtr>> enums
        )
        {
            var runtime = this.Runtime;

            var address = this.ToStaticAddress(pointer);

            //xml.WriteComment(_($" {address:X8} "));

            var columns = new List<KeyValuePair<ParseColumn, string>>();
            var columnSize =
                runtime.Is32Bit == false
                    ? Marshal.SizeOf(typeof(ParseColumn))
                    : Marshal.SizeOf(typeof(x32.ParseColumn));

            var columnPointer = pointer;
            while (true)
            {
                var columnAddress = this.ToStaticAddress(columnPointer);
                var column =
                    runtime.Is32Bit == false
                        ? runtime.ReadStructure<ParseColumn>(columnPointer)
                        : runtime.ReadStructure<x32.ParseColumn>(columnPointer).Upgrade();
                var name =
                    column.NamePointer != default
                        ? runtime.ReadStringZ(column.NamePointer, Encoding.ASCII)
                        : null;
                if (column.Type == 0 && string.IsNullOrEmpty(name) == true)
                {
                    break;
                }
                columns.Add(new KeyValuePair<ParseColumn, string>(column, name));
                columnPointer += columnSize;
            }

            foreach (var kv in columns)
            {
                var column = kv.Key;
                var name = kv.Value;

                xml.WriteStartElement("column");
                xml.WriteAttributeString("raw_type", _($"{column.Type}"));
                xml.WriteAttributeString("raw_parameter0", _($"{column.Parameter0.UInt64}"));

                if (string.IsNullOrEmpty(name) == false)
                {
                    xml.WriteAttributeString("name", name);
                }

                // Debug: show token before lookup
                if (column.Token >= Parse.GlobalTokens.Count)
                {
                    Console.WriteLine(
                        $"  ERROR: Invalid token {column.Token} for column '{name}', Type=0x{column.Type:X}"
                    );
                    xml.WriteAttributeString("type", $"UNKNOWN_TOKEN_{column.Token}");
                    xml.WriteEndElement();
                    continue;
                }

                var token = Parse.GlobalTokens.GetToken(column.Token);
                xml.WriteAttributeString(
                    "hash_parameter0_kind",
                    token.GetParameter(column.Flags, 0).ToString());

                var (tokenName, tokenFlags) = column.GetTokenNameAndFlags();

                xml.WriteAttributeString("type", tokenName);

                if (column.Token != 0 && column.Token != 1 && column.Token != 2)
                {
                    xml.WriteElementString("offset", _($"{column.Offset}"));
                }

                if (column.Flags.HasAny(Parse.ColumnFlags.PARSETABLE_INFO) == true)
                {
                    xml.WriteStartElement("flags");
                    xml.WriteElementString("flag", "PARSETABLE_INFO");
                    xml.WriteEndElement();

                    var formatString = this.ReadTokenString(column.FormatStringPointer);
                    if (string.IsNullOrEmpty(formatString) == false)
                    {
                        xml.WriteStartElement("format_strings");
                        FormatStringExporter.Export(xml, formatString);
                        xml.WriteEndElement();
                    }
                }
                else
                {
                    if ((tokenFlags & ColumnFlagsInfo.Mask) != 0)
                    {
                        xml.WriteStartElement("flags");
                        foreach (var kv2 in ColumnFlagsInfo.GetEnumerable())
                        {
                            if ((tokenFlags & kv2.Key) != 0)
                            {
                                xml.WriteElementString("flag", kv2.Value);
                            }
                        }
                        xml.WriteEndElement();
                    }

                    if (column.Flags.HasAny(Parse.ColumnFlags.REDUNDANTNAME) == true)
                    {
                        var aliased = columns
                            .Where(c => c.Key != column)
                            .Where(c =>
                                c.Key.Flags.HasAny(Parse.ColumnFlags.REDUNDANTNAME) == false
                            )
                            .Where(c => c.Key.Offset == column.Offset)
                            .Where(c => c.Key.Token == column.Token)
                            .FirstOrDefault(c =>
                                c.Key.Token != 23
                                || c.Key.Parameter0.Pointer == column.Parameter0.Pointer
                            );
                        if (aliased.Key != null)
                        {
                            xml.WriteElementString("redundant_name", aliased.Value);
                        }
                    }
                    //else
                    {
                        if (column.MinBits != 0)
                        {
                            if (column.Token == 7)
                            {
                                xml.WriteElementString(
                                    "float_rounding",
                                    _FloatRounding[column.MinBits]
                                );
                            }
                            else
                            {
                                xml.WriteElementString("min_bits", _($"{column.MinBits}"));
                            }
                        }

                        switch (token.GetParameter(column.Flags, 0))
                        {
                            case Parse.ColumnParameter.NumberOfElements:
                            {
                                xml.WriteElementString(
                                    "num_elements",
                                    _($"{column.Parameter0.Int32}")
                                );
                                break;
                            }

                            case Parse.ColumnParameter.Default:
                            {
                                if (column.Parameter0.Int32 != 0)
                                {
                                    xml.WriteElementString(
                                        "default",
                                        _($"{column.Parameter0.Int32}")
                                    );
                                }
                                break;
                            }

                            case Parse.ColumnParameter.StringLength:
                            {
                                xml.WriteElementString(
                                    "string_length",
                                    _($"{column.Parameter0.Int32}")
                                );
                                break;
                            }

                            case Parse.ColumnParameter.DefaultString:
                            {
                                if (column.Parameter0.Pointer != default)
                                {
                                    xml.WriteElementString(
                                        "default_string",
                                        runtime.ReadStringZ(
                                            column.Parameter0.Pointer,
                                            Encoding.ASCII
                                        ) ?? ""
                                    );
                                }
                                break;
                            }

                            case Parse.ColumnParameter.CommandString:
                            {
                                if (column.Parameter0.Pointer != default)
                                {
                                    xml.WriteElementString(
                                        "command_string",
                                        runtime.ReadStringZ(
                                            column.Parameter0.Pointer,
                                            Encoding.ASCII
                                        ) ?? ""
                                    );
                                }
                                break;
                            }

                            case Parse.ColumnParameter.Size:
                            {
                                xml.WriteElementString("size", _($"{column.Parameter0.Int32}"));
                                break;
                            }

                            case Parse.ColumnParameter.BitOffset:
                            {
                                xml.WriteElementString(
                                    "bit_offset",
                                    _($"{column.Parameter0.Int32}")
                                );
                                break;
                            }
                        }

                        switch (token.GetParameter(column.Flags, 1))
                        {
                            case Parse.ColumnParameter.StaticDefineList:
                            {
                                if (column.Parameter1.Pointer != default)
                                {
                                    xml.WriteStartElement("static_define_list");

                                    var external = enums.SingleOrDefault(e =>
                                        e.Value == column.Parameter1.Pointer
                                    );
                                    if (external.Key == default)
                                    {
                                        var staticAddress = this.ToStaticAddress(
                                            column.Parameter1.Pointer
                                        );
                                        xml.WriteComment(
                                            staticAddress != default
                                                ? $" dynamic enum? {staticAddress:X} "
                                                : $" dynamic enum? "
                                        );
                                    }
                                    else
                                    {
                                        xml.WriteAttributeString("external", external.Key);
                                    }

                                    //xml.WriteComment(string.Format(" {0:X8} ", column.Parameter1));
                                    xml.WriteEndElement();
                                }
                                break;
                            }

                            case Parse.ColumnParameter.DictionaryName:
                            {
                                if (column.Parameter1.Pointer != default)
                                {
                                    var value = runtime.ReadStringZ(
                                        column.Parameter1.Pointer,
                                        Encoding.ASCII
                                    );
                                    xml.WriteElementString("dictionary_name", value);
                                }
                                break;
                            }

                            case Parse.ColumnParameter.Subtable:
                            {
                                if (column.Parameter1.Pointer == default)
                                {
                                    xml.WriteElementString("subtable", "NULL?");
                                }
                                else
                                {
                                    xml.WriteStartElement("subtable");

                                    var parse = parses.SingleOrDefault(p =>
                                        p.Value == column.Parameter1.Pointer
                                    );
                                    if (parse.Name == null)
                                    {
                                        //xml.WriteElementString("subtable", "NOT FOUND? " + column.Parameter1.ToString("X*"));
                                        xml.WriteStartElement("table");
                                        Export(column.Parameter1.Pointer, xml, parses, enums);
                                        xml.WriteEndElement();
                                    }
                                    else
                                    {
                                        xml.WriteAttributeString("external", parse.Name);
                                    }

                                    xml.WriteEndElement();
                                }
                                break;
                            }
                        }

                        var format = column.Format & 0xFF;
                        if (format != 0)
                        {
                            string formatName;
                            if (
                                format < _FormatNames.Length
                                && (formatName = _FormatNames[format]) != null
                            )
                            {
                                xml.WriteElementString("format", formatName);
                            }
                            else
                            {
                                xml.WriteElementString("format_raw", _($"{format}"));
                            }
                        }

                        /* Star Trek Online replaces its format string with a
                         * stash table of parsed tokens.
                         */
                        string formatString = this.ReadTokenString(column.FormatStringPointer);
                        if (string.IsNullOrEmpty(formatString) == false)
                        {
                            xml.WriteStartElement("format_strings");
                            FormatStringExporter.Export(xml, formatString);
                            xml.WriteEndElement();
                        }
                    }
                }

                xml.WriteEndElement();
            }
        }
    }
}
