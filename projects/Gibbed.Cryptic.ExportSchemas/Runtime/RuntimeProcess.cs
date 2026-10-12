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
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Gibbed.Cryptic.ExportSchemas.Runtime
{
    internal class RuntimeProcess : RuntimeBase
    {
        private bool _Disposed;
        private Process _Process;
        private IntPtr _Handle = IntPtr.Zero;
        private bool _Is32Bit;
        private const int ReadCachePageSize = 4096;
        private readonly Dictionary<long, byte[]> _ReadCache = new Dictionary<long, byte[]>();

        public RuntimeProcess()
            : base(nameof(RuntimeProcess)) { }

        public Process Process
        {
            get { return this._Process; }
        }

        public override bool Is32Bit
        {
            get { return this._Is32Bit; }
        }
        public override IntPtr MainModuleBase =>
            this._Process?.MainModule?.BaseAddress ?? IntPtr.Zero;
        public override int MainModuleSize => this._Process?.MainModule?.ModuleMemorySize ?? 0;

        protected override void Dispose(bool disposing)
        {
            if (this._Disposed == false)
            {
                if (this._Handle != default)
                {
                    this.CloseProcess();
                }

                this._Disposed = true;
            }

            base.Dispose(disposing);
        }

        public bool OpenProcess(Process process)
        {
            if (this._Disposed == true)
            {
                throw new ObjectDisposedException(nameof(RuntimeProcess));
            }

            if (this._Handle != IntPtr.Zero)
            {
                Win32.CloseHandle(this._Handle);
                this._Process = null;
                this._Handle = IntPtr.Zero;
            }

            var handle = Win32.OpenProcess(Win32.ProcessAllAccess, false, (uint)process.Id);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            this._Process = process;
            this._Handle = handle;
            this._Is32Bit =
                Win32.IsWow64Process(handle, out var isWow64Process) == true
                && isWow64Process == true;
            return true;
        }

        public bool CloseProcess()
        {
            if (this._Disposed == true)
            {
                throw new ObjectDisposedException(nameof(RuntimeProcess));
            }

            if (this._Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("process handle is invalid");
            }

            var handle = this._Handle;
            this._Process = null;
            this._Handle = IntPtr.Zero;
            return Win32.CloseHandle(handle);
        }

        private bool SuspendThread(int id)
        {
            var handle = Win32.OpenThread(Win32.ThreadAllAccess, false, (uint)id);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var result = Win32.SuspendThread(handle) == 0xFFFFFFFF;
            Win32.CloseHandle(handle);
            return result;
        }

        private bool ResumeThread(int id)
        {
            var handle = Win32.OpenThread(Win32.ThreadAllAccess, false, (uint)id);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var result = Win32.ResumeThread(handle) == 0xFFFFFFFF;
            Win32.CloseHandle(handle);
            return result;
        }

        public bool SuspendThreads()
        {
            bool result = true;
            foreach (ProcessThread thread in this._Process.Threads)
            {
                if (this.SuspendThread(thread.Id) == false)
                {
                    result = false;
                }
            }
            return result;
        }

        public bool ResumeThreads()
        {
            bool result = true;
            foreach (ProcessThread thread in this._Process.Threads)
            {
                if (this.ResumeThread(thread.Id) == false)
                {
                    result = false;
                }
            }
            return result;
        }

        public override int Read(IntPtr address, byte[] buffer, int offset, int length)
        {
            if (this._Disposed == true)
            {
                throw new ObjectDisposedException(nameof(RuntimeProcess));
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

            if (this._Handle == default)
            {
                throw new InvalidOperationException("process handle is invalid");
            }

            /* Schema memory is immutable for the duration of an export, while
             * ReadStringZ historically issued one ReadProcessMemory call per
             * character.  Cache successful 4 KiB pages to make recursive
             * parser hashing practical on the 64-bit client. */
            var remaining = length;
            var destination = offset;
            var current = address.ToInt64();
            while (remaining > 0)
            {
                var pageAddress = current & ~(ReadCachePageSize - 1L);
                var pageOffset = (int)(current - pageAddress);
                var take = Math.Min(remaining, ReadCachePageSize - pageOffset);
                if (this._ReadCache.TryGetValue(pageAddress, out var page) == false)
                {
                    page = new byte[ReadCachePageSize];
                    if (this.ReadDirect(new IntPtr(pageAddress), page, 0, page.Length) != page.Length)
                    {
                        throw new InvalidOperationException("could not read cache page");
                    }
                    this._ReadCache.Add(pageAddress, page);
                }
                Buffer.BlockCopy(page, pageOffset, buffer, destination, take);
                current += take;
                destination += take;
                remaining -= take;
            }
            return length;
        }

        private int ReadDirect(IntPtr address, byte[] buffer, int offset, int length)
        {
            var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var size = (uint)length;
                var result = Win32.ReadProcessMemory(this._Handle, address,
                    bufferHandle.AddrOfPinnedObject() + offset, size, out var read);
                if (result == false) throw new Win32Exception();
                if (read != size) throw new InvalidOperationException($"only read {read} instead of {size}");
                return (int)read;
            }
            finally
            {
                bufferHandle.Free();
            }
        }

        public override int Write(IntPtr address, byte[] buffer, int offset, int length)
        {
            if (this._Disposed == true)
            {
                throw new ObjectDisposedException(nameof(RuntimeProcess));
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

            var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            uint size = (uint)length;
            var result = Win32.WriteProcessMemory(
                this._Handle,
                address,
                bufferHandle.AddrOfPinnedObject() + offset,
                size,
                out var written
            );

            bufferHandle.Free();

            if (result == false)
            {
                throw new Win32Exception();
            }

            if (written != size)
            {
                throw new InvalidOperationException($"only wrote {written} instead of {size}");
            }

            return (int)written;
        }
    }
}
