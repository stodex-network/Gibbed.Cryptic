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
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct ExpressionFunction
    {
        public int ArgumentCount; // Argc
        public int ExprCodeEnum; // Exprcodeenum
        public IntPtr Handler;
        public IntPtr Unknown010;
        public IntPtr NamePointer; // FuncName
        public IntPtr CommentPointer; // Comment
        public IntPtr SourceFilePointer; // Sourcefile
        public uint LineNumber; // Linenum
        public uint Unknown03C;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public ExpressionArgument[] Arguments; // Args

        public ExpressionArgument ReturnType; // ReturnType

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public IntPtr[] TagPointers; // Tags

        public float Cost; // Cost
        public uint Unknown5FC;
        public IntPtr ExprCodeNamePointer; // Exprcodename
        public IntPtr Unknown318;
        public uint Flags; // Funcflags
        public uint Unknown614;
        public IntPtr ParsedTagsPointer;
    }
}
