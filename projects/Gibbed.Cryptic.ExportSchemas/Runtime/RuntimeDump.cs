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
using System.Runtime.InteropServices;
using System.Text;

namespace Gibbed.Cryptic.ExportSchemas.Runtime
{
    internal class RuntimeDump : RuntimeBase
    {
        private const uint MinidumpSignature = 0x504D444D; // "PMDM" - "MDMP" in little endian
        private const ushort MinidumpVersion = 0xA793;

        private bool _Disposed;
        private FileStream _Stream;
        private BinaryReader _Reader;
        private bool _Is32Bit;
        private List<MemoryRange> _MemoryRanges;
        private IntPtr _MainModuleBase;
        private int _MainModuleSize;

        public RuntimeDump()
            : base(nameof(RuntimeDump))
        {
            this._MemoryRanges = new List<MemoryRange>();
        }

        public override bool Is32Bit => this._Is32Bit;

        public override IntPtr MainModuleBase => this._MainModuleBase;
        public override int MainModuleSize => this._MainModuleSize;

        protected override void Dispose(bool disposing)
        {
            if (this._Disposed == false)
            {
                if (disposing)
                {
                    this._Reader?.Dispose();
                    this._Stream?.Dispose();
                }
                this._Disposed = true;
            }
            base.Dispose(disposing);
        }

        public bool OpenDump(string path)
        {
            if (this._Disposed)
            {
                throw new ObjectDisposedException(nameof(RuntimeDump));
            }

            try
            {
                this._Stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                this._Reader = new BinaryReader(this._Stream);

                // Read MINIDUMP_HEADER
                var signature = this._Reader.ReadUInt32();
                if (signature != MinidumpSignature)
                {
                    Console.WriteLine($"Invalid minidump signature: 0x{signature:X8}");
                    return false;
                }

                var version = this._Reader.ReadUInt16();
                var implementationVersion = this._Reader.ReadUInt16();
                var numberOfStreams = this._Reader.ReadUInt32();
                var streamDirectoryRva = this._Reader.ReadUInt32();
                var checksum = this._Reader.ReadUInt32();
                var timeDateStamp = this._Reader.ReadUInt32();
                var flags = this._Reader.ReadUInt64();

                // Read stream directory
                this._Stream.Position = streamDirectoryRva;
                for (uint i = 0; i < numberOfStreams; i++)
                {
                    var streamType = this._Reader.ReadUInt32();
                    var dataSize = this._Reader.ReadUInt32();
                    var rva = this._Reader.ReadUInt32();

                    var nextPos = this._Stream.Position;

                    switch (streamType)
                    {
                        case 4: // ModuleListStream
                            ParseModuleList(rva, dataSize);
                            break;
                        case 5: // Memory64ListStream - actually this is wrong
                            break;
                        case 9: // Memory64ListStream
                            ParseMemory64List(rva, dataSize);
                            break;
                        case 7: // SystemInfoStream
                            ParseSystemInfo(rva);
                            break;
                    }

                    this._Stream.Position = nextPos;
                }

                // If we have a main module, use it. Otherwise, infer from memory ranges
                if (this._MainModuleBase == IntPtr.Zero && this._MemoryRanges.Count > 0)
                {
                    // Find largest memory range as main module (heuristic)
                    MemoryRange largest = this._MemoryRanges[0];
                    foreach (var range in this._MemoryRanges)
                    {
                        if (range.Size > largest.Size)
                        {
                            largest = range;
                        }
                    }
                    this._MainModuleBase = new IntPtr((long)largest.VirtualAddress);
                    this._MainModuleSize = (int)largest.Size;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open dump: {ex.Message}");
                return false;
            }
        }

        private void ParseSystemInfo(uint rva)
        {
            this._Stream.Position = rva;
            var processorArchitecture = this._Reader.ReadUInt16();
            // 0 = x86, 9 = AMD64
            this._Is32Bit = processorArchitecture == 0;
        }

        private void ParseModuleList(uint rva, uint dataSize)
        {
            this._Stream.Position = rva;
            var numberOfModules = this._Reader.ReadUInt32();

            // MINIDUMP_MODULE structure (x64/x86 agnostic for base address)
            for (uint i = 0; i < numberOfModules; i++)
            {
                var baseOfImage = this._Reader.ReadUInt64();
                var sizeOfImage = this._Reader.ReadUInt32();
                var checksum = this._Reader.ReadUInt32();
                var timeDateStamp = this._Reader.ReadUInt32();
                var moduleNameRva = this._Reader.ReadUInt32();
                // Skip the rest of MINIDUMP_MODULE:
                // VS_FIXEDFILEINFO (52 bytes) + CvRecord (8) + MiscRecord (8) + Reserved0 (8) + Reserved1 (8) = 84 bytes
                this._Reader.ReadBytes(84);

                // Read module name
                var savedPos = this._Stream.Position;
                this._Stream.Position = moduleNameRva;
                var nameLength = this._Reader.ReadUInt32();
                var nameBytes = this._Reader.ReadBytes((int)nameLength);
                var moduleName = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
                this._Stream.Position = savedPos;

                // Check if this is the main module (GameClient.exe)
                if (moduleName.EndsWith("GameClient.exe", StringComparison.OrdinalIgnoreCase))
                {
                    this._MainModuleBase = new IntPtr((long)baseOfImage);
                    this._MainModuleSize = (int)sizeOfImage;
                }
            }
        }

        private void ParseMemory64List(uint rva, uint dataSize)
        {
            this._Stream.Position = rva;
            var numberOfMemoryRanges = this._Reader.ReadUInt64();
            var baseRva = this._Reader.ReadUInt64();

            var currentFileOffset = baseRva;

            for (ulong i = 0; i < numberOfMemoryRanges; i++)
            {
                var startOfMemoryRange = this._Reader.ReadUInt64();
                var dataSize64 = this._Reader.ReadUInt64();

                this._MemoryRanges.Add(
                    new MemoryRange
                    {
                        VirtualAddress = startOfMemoryRange,
                        Size = dataSize64,
                        FileOffset = currentFileOffset,
                    }
                );

                currentFileOffset += dataSize64;
            }
        }

        public override int Read(IntPtr address, byte[] buffer, int offset, int length)
        {
            if (this._Disposed)
            {
                throw new ObjectDisposedException(nameof(RuntimeDump));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (length < 0 || offset + length > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            var virtualAddress = (ulong)address.ToInt64();
            var totalRead = 0;

            while (length > 0)
            {
                var found = false;

                foreach (var range in this._MemoryRanges)
                {
                    if (
                        virtualAddress >= range.VirtualAddress
                        && virtualAddress < range.VirtualAddress + range.Size
                    )
                    {
                        var offsetInRange = virtualAddress - range.VirtualAddress;
                        var availableInRange = (int)
                            Math.Min((ulong)length, range.Size - offsetInRange);

                        this._Stream.Position = (long)(range.FileOffset + offsetInRange);
                        var read = this._Stream.Read(buffer, offset, availableInRange);

                        if (read == 0)
                        {
                            throw new InvalidOperationException(
                                $"Failed to read from dump at virtual address 0x{virtualAddress:X16}"
                            );
                        }

                        totalRead += read;
                        offset += read;
                        length -= read;
                        virtualAddress += (ulong)read;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new InvalidOperationException(
                        $"Virtual address 0x{virtualAddress:X16} not found in dump memory ranges"
                    );
                }
            }

            return totalRead;
        }

        public override int Write(IntPtr address, byte[] buffer, int offset, int length)
        {
            throw new NotSupportedException("Cannot write to a dump file");
        }

        private struct MemoryRange
        {
            public ulong VirtualAddress;
            public ulong Size;
            public ulong FileOffset;
        }
    }
}
