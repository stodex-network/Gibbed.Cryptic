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
using Gibbed.Cryptic.ExportSchemas.Runtime;

namespace Gibbed.Cryptic.ExportSchemas.Locators.x64
{
    internal static class EnumTableInfosLocator1
    {
        public static bool Locate(RuntimeBase runtime, out IntPtr result)
        {
            // x64 pattern for StaticDefine_LookupByName (FUN_1404dac20)
            // This finds the name→enum stash table at DAT_143371b28
            //
            // 1404dac20: SUB RSP,0x28                        ; 48 83 EC 28
            // 1404dac24: MOV RDX,RCX                         ; 48 8B D1
            // 1404dac27: MOV qword ptr [RSP + 0x38],0x0      ; 48 C7 44 24 38 00 00 00 00
            // 1404dac30: MOV RCX,qword ptr [0x143371b28]     ; 48 8B 0D xx xx xx xx
            // 1404dac37: TEST RCX,RCX                        ; 48 85 C9
            // 1404dac3a: JZ 0x1404dac50                      ; 74 14

            var codePattern = new ByteSearch.Pattern()
            {
                // SUB RSP,0x28
                new byte[] { 0x48, 0x83, 0xEC, 0x28 },
                // MOV RDX,RCX
                new byte[] { 0x48, 0x8B, 0xD1 },
                // MOV qword ptr [RSP + 0x38],0x0
                new byte[] { 0x48, 0xC7, 0x44, 0x24, 0x38, 0x00, 0x00, 0x00, 0x00 },
                // MOV RCX,qword ptr [RIP+offset]
                new byte[] { 0x48, 0x8B, 0x0D },
                ByteSearch.AnyBytes(4),
                // TEST RCX,RCX
                new byte[] { 0x48, 0x85, 0xC9 },
            };

            if (runtime.Search(codePattern, out var codeOffset) == false)
            {
                result = default;
                return false;
            }

            // SUB: 4, MOV RDX: 3, MOV qword: 9 = 16 bytes before MOV RCX
            // MOV RCX,qword ptr [RIP+offset] starts at offset 16
            // The RIP-relative offset is at offset 19 (16 + 3 for the opcode 48 8B 0D)
            // The instruction is 7 bytes, so next instruction is at offset 23
            var movInstructionOffset = codeOffset + 16;
            var ripRelativeOffset = runtime.ReadValueS32(movInstructionOffset + 3);
            var nextInstructionAddress = movInstructionOffset + 7;
            var pointer = new IntPtr(nextInstructionAddress.ToInt64() + ripRelativeOffset);

            if (pointer == IntPtr.Zero)
            {
                throw new InvalidOperationException();
            }

            result = pointer;
            return true;
        }
    }
}
