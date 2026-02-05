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
    internal static class ExpressionFunctionTableLocator1
    {
        public static bool Locate(RuntimeBase runtime, out IntPtr result)
        {
            // x64 pattern for expression function table initialization
            // Looking for a function that checks if the global pointer is null and creates a stash table
            //
            // Pattern from function at 0x140361be0:
            // 140361bf2: CMP qword ptr [0x142f25688], 0x0  ; checking if table exists
            // ...
            // 140361c62: CALL StashTable_Create
            // ...
            // 140361c90: MOV qword ptr [0x142f25688], RAX  ; store the new table
            //
            // The pattern searches for:
            // - CMP qword ptr [RIP+offset], 0x0  ; 48 83 3D xx xx xx xx 00
            // - JNZ to end of function          ; 0F 85 xx xx xx xx (large jump)
            // - Then after stash table create:
            // - MOV qword ptr [RIP+offset], RAX ; 48 89 05 xx xx xx xx

            var codePattern = new ByteSearch.Pattern()
            {
                // mov rax, rsp           ; 48 8B C4
                new byte[] { 0x48, 0x8B, 0xC4 },
                // push rbp               ; 55
                new byte[] { 0x55 },
                // lea rbp, [rax-278h]    ; 48 8D A8 88 FD FF FF (or similar)
                new byte[] { 0x48, 0x8D },
                ByteSearch.AnyBytes(5),
                // sub rsp, 370h          ; 48 81 EC 70 03 00 00
                new byte[] { 0x48, 0x81, 0xEC },
                ByteSearch.AnyBytes(4),
                // cmp qword ptr [addr], 0 ; 48 83 3D xx xx xx xx 00
                new byte[] { 0x48, 0x83, 0x3D },
                ByteSearch.AnyBytes(4),
                new byte[] { 0x00 },
            };

            if (runtime.Search(codePattern, out var codeOffset) == false)
            {
                result = default;
                return false;
            }

            // Extract the RIP-relative offset from the CMP instruction
            // CMP qword ptr [RIP+offset], 0 is 8 bytes: 48 83 3D xx xx xx xx 00
            // The offset is at codeOffset + pattern_length_before_cmp + 3
            // Pattern before CMP: 3 + 1 + 7 + 7 = 18 bytes, CMP starts at +18
            var cmpInstructionOffset = codeOffset + 18;
            var ripRelativeOffset = runtime.ReadValueS32(cmpInstructionOffset + 3);
            // Next instruction is 8 bytes after CMP start
            var nextInstructionAddress = cmpInstructionOffset + 8;
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
