using System;
using System.Runtime.InteropServices;
using Gibbed.Cryptic.FileFormats.Parse;

namespace Gibbed.Cryptic.ExportSchemas.Natives.x64
{
    // Current x64 client layout; 0x38-byte stride verified from live tables.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal class ParseColumn
    {
        public IntPtr NamePointer;                    // 0x00
        public ulong Type;                            // 0x08
        public uint Offset;                           // 0x10 (+ alignment)
        public ParseColumnParameterValue Parameter0;  // 0x18
        public ParseColumnParameterValue Parameter1;  // 0x20
        public uint Format;                           // 0x28 (+ alignment)
        public IntPtr FormatStringPointer;            // 0x30

        public byte MinBits => (byte)((this.Type >> 8) & 0xFF);
        public byte FloatRounding => this.MinBits;
        public byte Token => (byte)(this.Type & 0xFF);
        public ColumnFlags Flags => (ColumnFlags)(this.Type & ~0xFFFFUL);

        public (string, ColumnFlags) GetTokenNameAndFlags()
        {
            var flags = this.Flags;
            var token = GlobalTokens.GetToken(this.Token);
            string name;
            if ((flags & ColumnFlags.EARRAY) != 0)
            {
                name = (flags & ColumnFlags.INDIRECT) != 0
                    ? token.NameIndirectArray
                    : token.NameDirectArray;
            }
            else if ((flags & ColumnFlags.FIXED_ARRAY) != 0)
            {
                name = (flags & ColumnFlags.INDIRECT) != 0
                    ? token.NameIndirectFixedArray
                    : token.NameDirectFixedArray;
            }
            else
            {
                name = (flags & ColumnFlags.INDIRECT) != 0
                    ? token.NameIndirectValue
                    : token.NameDirectValue;
            }
            if (name == null)
            {
                name = token.NameDirectValue;
            }
            return (name, flags);
        }
    }
}
