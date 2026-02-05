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

namespace Gibbed.Cryptic.ExportSchemas.Natives.x64
{
    // x64 StashTable header structure (total 0x70 bytes based on allocation)
    // Analyzed from StashTable_Create and StashTable_Lookup in Ghidra
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct StashTableHeader
    {
        public IntPtr EntriesPointer; // 0x00 - pointer to entry array (*param_1)
        public long Type; // 0x08 - comparison type (param_1[1])
        public uint Mask; // 0x10 - mask for hash (*(uint*)(param_1+2))
        public uint Unknown14; // 0x14 - padding/flags
        public uint Unknown18; // 0x18
        public int AllocatedCount; // 0x1C - number of allocated entries
    }
}
