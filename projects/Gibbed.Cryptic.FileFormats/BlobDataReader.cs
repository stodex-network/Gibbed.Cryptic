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
using System.Text;
using Gibbed.IO;

namespace Gibbed.Cryptic.FileFormats
{
    public class BlobDataReader : Serialization.IFileReader
    {
        private readonly Stream _Input;

        private readonly bool _IsClient;

        public bool IsClient
        {
            get { return this._IsClient; }
        }

        private readonly bool _IsServer;

        public bool IsServer
        {
            get { return this._IsServer; }
        }

        private readonly Func<int, string> _GetFileNameFromIndex;

        public BlobDataReader(Stream input, bool isClient, bool isServer, Func<int, string> getFileNameFromIndex)
        {
            this._Input = input;
            this._IsClient = isClient;
            this._IsServer = isServer;
            this._GetFileNameFromIndex = getFileNameFromIndex;
        }

        // ---- Optional underrun diagnostics (stodex fork) ------------------------------------------------------
        // A length-delimited structure that was read with fewer bytes than its size has its remainder skipped (see
        // ReadValueStructure). When SkipObserver is set it is called as (stream position before the skip, bytes
        // skipped, field path) at every such skip. The path is built from a stack of frames, one per structure being
        // read, that exists only while an observer is set; with no observer every hook is a single null check and no
        // frame, list or string is allocated. Column counting works because the generated Deserialize calls exactly one
        // public Read* method per column, in field declaration order (columns hidden by IsClient/IsServer are not
        // read, so the name is exact when both flags are true and best effort otherwise).
        private Action<long, long, string> _Observer;

        private List<DiagFrame> _Frames;

        private sealed class DiagFrame
        {
            public Type Type;
            public int Columns;
            public int Element = -1;
            public int Suppress;
        }

        private static readonly Dictionary<Type, string[]> _FieldNames = new Dictionary<Type, string[]>();

        /// <summary>
        /// Called at each underrun skip as (position before the skip, skipped byte count, path), for example
        /// "GroupDef.Properties[3].Expression": the last column read by the structure that left bytes unread, reached
        /// through the named fields and array indices that enclose it. Not thread safe; set before reading.
        /// </summary>
        public Action<long, long, string> SkipObserver
        {
            get { return this._Observer; }
            set
            {
                this._Observer = value;
                this._Frames = value == null ? null : new List<DiagFrame>();
            }
        }

        private void NoteColumn()
        {
            var frames = this._Frames;
            if (frames.Count > 0)
            {
                var top = frames[frames.Count - 1];
                if (top.Suppress == 0)
                {
                    top.Columns++;
                }
            }
        }

        private DiagFrame PushFrame(Type type)
        {
            var frame = new DiagFrame() { Type = type };
            this._Frames.Add(frame);
            return frame;
        }

        private void PopFrame(DiagFrame frame)
        {
            var frames = this._Frames;
            if (frames == null) { return; }
            var index = frames.LastIndexOf(frame);
            if (index >= 0) { frames.RemoveRange(index, frames.Count - index); }
        }

        private DiagFrame BeginElements()
        {
            var frames = this._Frames;
            if (frames == null || frames.Count == 0) { return null; }
            var top = frames[frames.Count - 1];
            top.Suppress++;
            return top;
        }

        private static void EndElements(DiagFrame frame)
        {
            if (frame != null) { frame.Suppress--; frame.Element = -1; }
        }

        private static string ColumnName(Type type, int column)
        {
            string[] names;
            lock (_FieldNames)
            {
                if (_FieldNames.TryGetValue(type, out names) == false)
                {
                    var fields = type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                    names = new string[fields.Length];
                    for (int i = 0; i < fields.Length; i++) { names[i] = fields[i].Name; }
                    _FieldNames[type] = names;
                }
            }
            return column >= 0 && column < names.Length ? names[column] : "#" + column;
        }

        private string BuildPath()
        {
            var frames = this._Frames;
            if (frames == null || frames.Count == 0) { return null; }
            var sb = new StringBuilder();
            sb.Append(frames[0].Type.Name);
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                if (frame.Columns > 0)
                {
                    sb.Append('.').Append(ColumnName(frame.Type, frame.Columns - 1));
                    if (frame.Element >= 0 && i + 1 < frames.Count) { sb.Append('[').Append(frame.Element).Append(']'); }
                }
            }
            return sb.ToString();
        }

        private class ResourceLoader<TType> : Serialization.IStructure
            where TType : Serialization.IStructure, new()
        {
            public List<TType> List = new List<TType>();

            public void Deserialize(Serialization.IFileReader reader, object state)
            {
                this.List = reader.ReadListStructure<TType>(ParseSchemaFile.DefaultMaxArraySize, state);
            }

            public void Serialize(Serialization.IFileWriter writer, object state)
            {
                throw new NotSupportedException();
            }

            public void Deserialize(Serialization.IPacketReader reader, object state)
            {
                throw new NotSupportedException();
            }

            public void Serialize(Serialization.IPacketWriter writer, object state)
            {
                throw new NotSupportedException();
            }
        }

        public static TType LoadObject<TType>(Stream input,
                                              bool isClient,
                                              bool isServer,
                                              Func<int, string> getFileNameFromIndex)
            where TType : Serialization.IStructure, new()
        {
            var reader = new BlobDataReader(input, isClient, isServer, getFileNameFromIndex);
            return reader.ReadValueStructure<TType>(false, null);
        }

        public static List<TType> LoadResource<TType>(Stream input,
                                                      bool isClient,
                                                      bool isServer,
                                                      Func<int, string> getFileNameFromIndex)
            where TType : Serialization.IStructure, new()
        {
            var reader = new BlobDataReader(input, isClient, isServer, getFileNameFromIndex);
            var loader = reader.ReadValueStructure<ResourceLoader<TType>>(false, null);
            return loader.List;
        }

        private TType[] ReadNativeArray<TType>(
            int count,
            object state,
            Func<BlobDataReader, object, TType> readValue)
        {
            var array = new TType[count];
            var frame = this._Observer != null ? this.BeginElements() : null;
            for (uint i = 0; i < count; i++)
            {
                if (frame != null) { frame.Element = (int)i; }
                array[i] = readValue(this, state);
            }
            EndElements(frame);
            return array;
        }

        private List<TType> ReadNativeList<TType>(
            int maxCount,
            object state,
            Func<BlobDataReader, object, TType> readValue)
        {
            var count = this._Input.ReadValueU32();
            if (count > maxCount)
            {
                throw new FormatException(string.Format("list exceeds maximum array size ({0} > {1})", count, maxCount));
            }

            var list = new List<TType>();
            var frame = this._Observer != null ? this.BeginElements() : null;
            for (uint i = 0; i < count; i++)
            {
                if (frame != null) { frame.Element = (int)i; }
                list.Add(readValue(this, state));
            }
            EndElements(frame);
            return list;
        }

        public byte ReadValueByte(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadValueU8();
        }

        public byte[] ReadArrayByte(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            var array = new byte[count];
            this._Input.Read(array, 0, count);
            return array;
        }

        public List<byte> ReadListByte(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueByte(b));
        }

        public short ReadValueInt16(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return (short)this._Input.ReadValueS32();
        }

        public short[] ReadArrayInt16(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueInt16(b));
        }

        public List<short> ReadListInt16(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueInt16(b));
        }

        public int ReadValueInt32(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadValueS32();
        }

        public int[] ReadArrayInt32(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueInt32(b));
        }

        public List<int> ReadListInt32(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueInt32(b));
        }

        public long ReadValueInt64(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadValueS64();
        }

        public long[] ReadArrayInt64(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueInt64(b));
        }

        public List<long> ReadListInt64(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueInt64(b));
        }

        public float ReadValueFloat(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadValueF32();
        }

        public float[] ReadArrayFloat(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueFloat(b));
        }

        public List<float> ReadListFloat(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueFloat(b));
        }

        public string ReadValueString(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadStringPascalUncapped();
        }

        public string[] ReadArrayString(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueString(b));
        }

        public List<string> ReadListString(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueString(b));
        }

        public string ReadValueCurrentFile(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            var start = this._Input.Position;
            var index = this._Input.ReadValueU32();

            if (index == 0)
            {
                return null;
            }

            if (index == 1)
            {
                return this._Input.ReadStringPascalUncapped();
            }

            try
            {
                return this._GetFileNameFromIndex((int)(index - 2));
            }
            catch (KeyNotFoundException) when (this._Input.CanSeek == true)
            {
                // Some current resources (notably Materials.bin) serialize a
                // CURRENTFILE column as the direct Pascal-string form even
                // though the runtime parse descriptor marks it indirect.
                // An inline string's first four bytes cannot be a valid file
                // catalogue index. Rewind and consume the direct wire form.
                this._Input.Position = start;
                return this._Input.ReadStringPascalUncapped();
            }
        }

        public string[] ReadArrayCurrentFile(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueCurrentFile(b));
        }

        public List<string> ReadListCurrentFile(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueCurrentFile(b));
        }

        public int ReadValueTimestamp(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadValueS32();
        }

        public int[] ReadArrayTimestamp(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueTimestamp(b));
        }

        public List<int> ReadListTimestamp(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueTimestamp(b));
        }

        public int ReadValueLineNumber(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadValueS32();
        }

        public int[] ReadArrayLineNumber(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueLineNumber(b));
        }

        public List<int> ReadListLineNumber(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueLineNumber(b));
        }

        public bool ReadValueBoolean(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            throw new NotImplementedException();
        }

        public bool[] ReadArrayBoolean(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueBoolean(b));
        }

        public List<bool> ReadListBoolean(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueBoolean(b));
        }

        public object ReadValueNoAST(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            throw new NotImplementedException();
        }

        public object[] ReadArrayNoAST(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            throw new NotImplementedException();
        }

        public List<object> ReadListNoAST(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            throw new NotImplementedException();
        }

        public bool ReadValueBooleanFlag(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadValueB8();
        }

        public bool[] ReadArrayBooleanFlag(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueBooleanFlag(b));
        }

        public List<bool> ReadListBooleanFlag(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueBooleanFlag(b));
        }

        public float ReadValueQUATPYR(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadValueF32();
        }

        public float[] ReadArrayQUATPYR(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueQUATPYR(b));
        }

        public List<float> ReadListQUATPYR(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueQUATPYR(b));
        }

        public MATPYR ReadValueMATPYR(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return new MATPYR()
            {
                X = this._Input.ReadValueF32(),
                Y = this._Input.ReadValueF32(),
                Z = this._Input.ReadValueF32(),
            };
        }

        public MATPYR[] ReadArrayMATPYR(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueMATPYR(b));
        }

        public List<MATPYR> ReadListMATPYR(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueMATPYR(b));
        }

        public string ReadValueFilename(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadStringPascalUncapped();
        }

        public string[] ReadArrayFilename(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueFilename(b));
        }

        public List<string> ReadListFilename(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueFilename(b));
        }

        public string ReadValueReference(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadStringPascalUncapped();
        }

        public string[] ReadArrayReference(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueReference(b));
        }

        public List<string> ReadListReference(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueReference(b));
        }

        public FunctionCall ReadValueFunctionCall(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            throw new NotImplementedException();
        }

        public FunctionCall[] ReadArrayFunctionCall(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueFunctionCall(b));
        }

        public List<FunctionCall> ReadListFunctionCall(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueFunctionCall(b));
        }

        private Serialization.IStructure ReadValueStructure(
            Type type,
            bool optional,
            object state)
        {
            if (optional == true)
            {
                var hasValue = this._Input.ReadValueU32();
                if (hasValue == 0)
                {
                    return null;
                }

                if (hasValue != 1)
                {
                    throw new FormatException("invalid optional data for structure");
                }
            }

            var dataSize = this._Input.ReadValueU32();

            var start = this._Input.Position;
            var end = this._Input.Position + dataSize;

            var instance = (Serialization.IStructure)Activator.CreateInstance(type);
            var frame = this._Observer != null ? this.PushFrame(type) : null;
            try
            {
                instance.Deserialize(this, state);

                if (this._Input.Position < end)
                {
                    // Current resources can retain trailing/versioned fields that
                    // are not represented in the registered runtime parse table.
                    // Structure records are length-delimited, so skip them safely.
                    var observer = this._Observer;
                    if (observer != null)
                    {
                        observer(this._Input.Position, end - this._Input.Position, this.BuildPath());
                    }
                    this._Input.Position = end;
                }
            }
            finally
            {
                if (frame != null) { this.PopFrame(frame); }
            }

            if (this._Input.Position > end)
            {
                throw new FormatException(string.Format(
                    "buffer overrun while deserializing {0} (start 0x{1:X}, expected end 0x{2:X}, actual 0x{3:X})",
                    type.FullName,
                    start,
                    end,
                    this._Input.Position));
            }

            return instance;
        }

        public TType ReadValueStructure<TType>(bool optional, object state)
            where TType : Serialization.IStructure, new()
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return (TType)ReadValueStructure(typeof(TType), optional, state);
        }

        public TType[] ReadArrayStructure<TType>(int count, object state)
            where TType : Serialization.IStructure, new()
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueStructure<TType>(false, b));
        }

        public List<TType> ReadListStructure<TType>(int maxCount, object state)
            where TType : Serialization.IStructure, new()
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueStructure<TType>(false, b));
        }

        public List<TType> ReadListEmbeddedStructure<TType>(int maxCount, object state)
            where TType : Serialization.IStructure, new()
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) =>
                {
                    var instance = new TType();
                    var frame = a._Observer != null ? a.PushFrame(typeof(TType)) : null;
                    try
                    {
                        instance.Deserialize(a, b);
                    }
                    finally
                    {
                        if (frame != null) { a.PopFrame(frame); }
                    }
                    return instance;
                });
        }

        public object ReadValuePolymorph(Type[] validTypes, bool optional, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            if (optional == true)
            {
                var hasValue = this._Input.ReadValueU32();
                if (hasValue == 0)
                {
                    return null;
                }

                if (hasValue != 1)
                {
                    throw new FormatException("invalid optional data for polymorph");
                }
            }

            var index = this._Input.ReadValueS32();
            var type = validTypes[index];
            return ReadValueStructure(type, false, state);
        }

        public object[] ReadArrayPolymorph(Type[] validTypes, int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValuePolymorph(validTypes, false, b));
        }

        public List<object> ReadListPolymorph(Type[] validTypes, int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValuePolymorph(validTypes, false, b));
        }

        public StashTable ReadValueStashTable(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            throw new NotImplementedException();
        }

        public StashTable[] ReadArrayStashTable(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueStashTable(b));
        }

        public List<StashTable> ReadListStashTable(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueStashTable(b));
        }

        public uint ReadValueBit(int bitOffset, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this._Input.ReadValueU32();
        }

        public uint[] ReadArrayBit(int bitOffset, int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueBit(bitOffset, b));
        }

        public List<uint> ReadListBit(int bitOffset, int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueBit(bitOffset, b));
        }

        public MultiValue ReadValueMultiValue(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            object arg;

            var name = this._Input.ReadString(4, true, Encoding.ASCII);
            MultiValueOpcode op;
            if (MultiValue.TryParseOpcode(name, out op) == false)
            {
                throw new FormatException("invalid or unknown multivalue opcode");
            }

            // ReSharper disable BitwiseOperatorOnEnumWithoutFlags
            switch (op & MultiValueOpcode.TypeMask) // ReSharper restore BitwiseOperatorOnEnumWithoutFlags
            {
                case MultiValueOpcode.NON:
                {
                    arg = null;
                    break;
                }

                case MultiValueOpcode.StaticVariable:
                {
                    StaticVariableType sv;
                    if (MultiValue.TryParseStaticVariable(this._Input.ReadValueU32(), out sv) == false)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                "multival {0} had an unexpected static variable index",
                                op));
                    }
                    arg = sv;
                    break;
                }

                case MultiValueOpcode.INT:
                {
                    arg = this._Input.ReadValueS64();
                    break;
                }

                case MultiValueOpcode.FLT:
                {
                    arg = this._Input.ReadValueF64();
                    break;
                }

                case MultiValueOpcode.STR:
                {
                    arg = this._Input.ReadStringPascalUncapped();
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        string.Format(
                            "multival {0} had an unsupported argument data type",
                            op));
            }

            return new MultiValue()
            {
                Op = op,
                Arg = arg,
            };
        }

        public MultiValue[] ReadArrayMultiValue(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueMultiValue(b));
        }

        public List<MultiValue> ReadListMultiValue(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueMultiValue(state));
        }

        public TType ReadValueByteEnum<TType>(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return (TType)Enum.ToObject(typeof(TType), this._Input.ReadValueU8());
        }

        public TType[] ReadArrayByteEnum<TType>(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueByteEnum<TType>(b));
        }

        public List<TType> ReadListByteEnum<TType>(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueByteEnum<TType>(state));
        }

        public TType ReadValueInt16Enum<TType>(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return (TType)Enum.ToObject(typeof(TType), (short)this._Input.ReadValueS32());
        }

        public TType[] ReadArrayInt16Enum<TType>(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueInt16Enum<TType>(b));
        }

        public List<TType> ReadListInt16Enum<TType>(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueInt16Enum<TType>(state));
        }

        public TType ReadValueInt32Enum<TType>(object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return (TType)Enum.ToObject(typeof(TType), this._Input.ReadValueS32());
        }

        public TType[] ReadArrayInt32Enum<TType>(int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueInt32Enum<TType>(b));
        }

        public List<TType> ReadListInt32Enum<TType>(int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueInt32Enum<TType>(b));
        }

        public TType ReadValueBitEnum<TType>(int bitOffset, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return (TType)Enum.ToObject(typeof(TType), this._Input.ReadValueU32());
        }

        public TType[] ReadArrayBitEnum<TType>(int bitOffset, int count, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeArray(
                count,
                state,
                (a, b) => a.ReadValueBitEnum<TType>(bitOffset, b));
        }

        public List<TType> ReadListBitEnum<TType>(int bitOffset, int maxCount, object state)
        {
            if (this._Observer != null) { this.NoteColumn(); }
            return this.ReadNativeList(
                maxCount,
                state,
                (a, b) => a.ReadValueBitEnum<TType>(bitOffset, b));
        }
    }
}
