using System;
using System.IO;
using Gibbed.Cryptic.FileFormats;
using Gibbed.Cryptic.FileFormats.Serialization;
public class ListBoundaryRecord : IStructure {
    public int Value;
    public void Deserialize(IFileReader reader, object state) { Value = reader.ReadValueInt32(state); }
    public void Serialize(IFileWriter writer, object state) { throw new NotSupportedException(); }
    public void Deserialize(IPacketReader reader, object state) { throw new NotSupportedException(); }
    public void Serialize(IPacketWriter writer, object state) { throw new NotSupportedException(); }
}
public static class ListBoundaryTest {
    public static void Main(string[] args) {
        var bundle = Path.GetFullPath(args[0]);
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) => {
            var path = Path.Combine(bundle, name.Name + ".dll");
            return File.Exists(path) ? System.Reflection.Assembly.LoadFrom(path) : null;
        };
        var generator = System.Reflection.Assembly.LoadFrom(Path.Combine(bundle, "Gibbed.Cryptic.GenerateSerializer.dll"));
        var column = new Gibbed.Cryptic.FileFormats.ParseSchema.Column {
            Token = Gibbed.Cryptic.FileFormats.Parse.TokenType.Structure,
            Flags = Gibbed.Cryptic.FileFormats.Parse.ColumnFlags.EARRAY
        };
        var method = generator.GetType("Gibbed.Cryptic.GenerateSerializer.Helpers").GetMethod("GetReadMethod");
        var reader = (System.Reflection.MethodInfo)method.Invoke(null, new object[] {column});
        if (reader.Name != "ReadListStructure") throw new Exception("Generator lost list boundaries");
        Run();
        int callbacks = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(bundle, "serializers/assemblies"), "STO.Serialization.*.dll")) {
            var assembly = System.Reflection.Assembly.LoadFrom(file);
            foreach (var type in assembly.GetTypes()) {
                var construct = type.GetMethod("ConstructLists", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (construct == null) continue;
                var instance = Activator.CreateInstance(type);
                construct.Invoke(instance, null);
                callbacks++;
            }
        }
        if (callbacks == 0) throw new Exception("No generated list callbacks tested");
        Console.WriteLine("PASS: structure list boundaries, overrun rejection and " + callbacks + " generated list callbacks");
    }
    public static void Run() {
        using (var stream = new MemoryStream()) {
            var writer = new BinaryWriter(stream);
            writer.Write(2); writer.Write(8); writer.Write(11); writer.Write(999);
            writer.Write(4); writer.Write(22); stream.Position = 0;
            var reader = new BlobDataReader(stream, true, false, i => null);
            var values = reader.ReadListStructure<ListBoundaryRecord>(10, null);
            if (values.Count != 2 || values[0].Value != 11 || values[1].Value != 22 || stream.Position != stream.Length)
                throw new Exception("Length boundary/trailing field handling failed");
        }
        using (var stream = new MemoryStream()) {
            var writer = new BinaryWriter(stream);
            writer.Write(1); writer.Write(0); writer.Write(11); stream.Position = 0;
            var reader = new BlobDataReader(stream, true, false, i => null);
            try { reader.ReadListStructure<ListBoundaryRecord>(10, null); }
            catch (FormatException) { return; }
            throw new Exception("Structure overrun was accepted");
        }
    }
}
