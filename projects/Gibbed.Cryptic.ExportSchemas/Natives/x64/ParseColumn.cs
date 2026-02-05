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
using System.Runtime.InteropServices;
using Gibbed.Cryptic.FileFormats;
using Gibbed.Cryptic.FileFormats.Parse;

namespace Gibbed.Cryptic.ExportSchemas.Natives.x64
{
    // x64 ParseColumn structure - 56 bytes (7 qwords) per column
    // Based on analysis: the stash stores column arrays directly
    // Stride confirmed from `puVar15 = puVar15 + 7` in Parser_RegisterType
    //
    // The GameClient.dmp appears to be a DEBUG/instrumented build with extra info.
    // We need to determine the correct layout from actual runtime data.
    //
    // Attempting layout where Type is at offset 0x18 (after 3 pointer fields):
    // - [0] 0x00: NamePointer
    // - [1] 0x08: FilePointer (debug - source file name)
    // - [2] 0x10: Unknown pointer
    // - [3] 0x18: Type (ulong with token in low 6 bits, flags in bits 8-31)
    // - [4] 0x20: Offset/Format (uint + uint)
    // - [5] 0x28: Parameter0
    // - [6] 0x30: Parameter1 or FormatStringPointer
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal class ParseColumn
    {
        public IntPtr NamePointer; // 0x00
        public IntPtr FilePointer; // 0x08 - Source file (debug)
        public IntPtr Unknown10; // 0x10 - Unknown pointer
        public ulong Type; // 0x18 - Type with token in low byte
        public uint Format; // 0x20
        public uint Offset; // 0x24
        public ParseColumnParameterValue Parameter0; // 0x28
        public IntPtr FormatStringPointer; // 0x30 - Using for param1 or format string

        // Total: 0x38 = 56 bytes = 7 qwords

        public byte Token => (byte)(this.Type & 0x3F);
        public ColumnFlags Flags => (ColumnFlags)((this.Type >> 8) & 0xFFFFFF);
        public int MinBits => (int)((this.Type >> 32) & 0x3F);

        // Compatibility - Parameter1 not separate in this layout
        public ParseColumnParameterValue Parameter1 =>
            new ParseColumnParameterValue { Pointer = this.FormatStringPointer };

        public (string, ColumnFlags) GetTokenNameAndFlags()
        {
            var flags = this.Flags;
            var token = GlobalTokens.GetToken(this.Token);
            var name = token.NameDirectValue;
            if (flags.HasAny(ColumnFlags.EARRAY))
            {
                name = "EA" + name;
                flags &= ~ColumnFlags.EARRAY;
            }
            return (name, flags);
        }
    }
}
