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
    /* 32-bit code embeds absolute addresses directly in the instruction
     * stream, so the 32-bit locators can simply read the four bytes as a
     * pointer. 64-bit code instead uses RIP-relative addressing: the
     * instruction stores a signed 32-bit displacement from the address of the
     * *following* instruction. Resolving it is base-independent, so this works
     * regardless of where the module happened to be loaded.
     */
    internal static class RipRelative
    {
        public static IntPtr Resolve(
            RuntimeBase runtime,
            IntPtr patternAddress,
            int displacementOffset,
            int instructionEnd)
        {
            var displacement = runtime.ReadValueS32(patternAddress + displacementOffset);
            return new IntPtr(patternAddress.ToInt64() + instructionEnd + displacement);
        }
    }
}
