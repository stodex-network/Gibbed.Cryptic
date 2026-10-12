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
    internal static class ParseTableInfosLocator1
    {
        /* Matches the lazy initializer for the "ff_ParseTableInfos" stash
         * table, the 64-bit counterpart of Locators/x32/ParseTableInfosLocator1:
         *
         *   mov  rcx, [rip+pointer1]              ; +0,  displacement at +3
         *   test rcx, rcx                         ; +7
         *   jne  already_created                  ; +10
         *   mov  r8d, <line>                      ; +16
         *   lea  rdx, [rip+"ff_ParseTableInfos"]  ; +22
         *   mov  ecx, 0x400                       ; +29
         *   call StashTableCreateWithStringKeys   ; +34
         *   mov  dl, 1                            ; +39
         *   mov  rcx, rax                         ; +41
         *   mov  rbx, rax                         ; +44
         *   call ...                              ; +47
         *   mov  rcx, [rip+...]                   ; +52
         *   mov  [rip+pointer2], rbx              ; +59, displacement at +62
         *
         * Unlike the 32-bit locator this cannot anchor on the address of the
         * "ff_ParseTableInfos" string, because the reference to it is
         * RIP-relative and therefore position-dependent. The `mov ecx, 0x400`
         * table size plays the same anchoring role it does in 32-bit.
         */
        public static bool Locate(RuntimeBase runtime, out IntPtr result)
        {
            var codePattern = new ByteSearch.Pattern()
            {
                new byte[] { 0x48, 0x8B, 0x0D }, // mov rcx, [rip+pointer1]
                ByteSearch.AnyBytes(4),
                new byte[] { 0x48, 0x85, 0xC9 }, // test rcx, rcx
                new byte[] { 0x0F, 0x85 }, // jne
                ByteSearch.AnyBytes(4),
                new byte[] { 0x41, 0xB8 }, // mov r8d, <line>
                ByteSearch.AnyBytes(4),
                new byte[] { 0x48, 0x8D, 0x15 }, // lea rdx, [rip+"ff_ParseTableInfos"]
                ByteSearch.AnyBytes(4),
                new byte[] { 0xB9, 0x00, 0x04, 0x00, 0x00 }, // mov ecx, 0x400
                new byte[] { 0xE8 }, // call
                ByteSearch.AnyBytes(4),
                new byte[] { 0xB2, 0x01 }, // mov dl, 1
                new byte[] { 0x48, 0x8B, 0xC8 }, // mov rcx, rax
                new byte[] { 0x48, 0x8B, 0xD8 }, // mov rbx, rax
                new byte[] { 0xE8 }, // call
                ByteSearch.AnyBytes(4),
                new byte[] { 0x48, 0x8B, 0x0D }, // mov rcx, [rip+...]
                ByteSearch.AnyBytes(4),
                new byte[] { 0x48, 0x89, 0x1D }, // mov [rip+pointer2], rbx
                ByteSearch.AnyBytes(4),
            };
            if (runtime.Search(codePattern, out var codeOffset) == false)
            {
                result = default;
                return false;
            }

            var pointer1 = RipRelative.Resolve(runtime, codeOffset, 3, 7);
            if (pointer1 == IntPtr.Zero)
            {
                throw new InvalidOperationException();
            }

            var pointer2 = RipRelative.Resolve(runtime, codeOffset, 62, 66);
            if (pointer2 == IntPtr.Zero)
            {
                throw new InvalidOperationException();
            }

            if (pointer1 != pointer2)
            {
                throw new InvalidOperationException();
            }

            result = pointer1;
            return true;
        }
    }
}
