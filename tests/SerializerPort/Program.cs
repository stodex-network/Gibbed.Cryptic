using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using Gibbed.Cryptic.FileFormats;
using Gibbed.Cryptic.GenerateSerializer;

// Public synthetic schemas exercise persisted type metadata and generated IL.
var temporary = Path.Combine(Path.GetTempPath(), "gibbed-net10-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var columns = new XElement("table",
        new XElement("column", new XAttribute("name", "Name"), new XAttribute("type", "STRING")),
        new XElement("column", new XAttribute("name", "bf"), new XAttribute("type", "INTFIXEDARRAY"), new XElement("num_elements", 1)),
        new XElement("column", new XAttribute("name", "Mode"), new XAttribute("type", "INT"), new XElement("default", 1),
            new XElement("static_define_list", new XAttribute("external", "Mode"))),
        new XElement("column", new XAttribute("name", "Modes"), new XAttribute("type", "INTARRAY"),
            new XElement("static_define_list", new XAttribute("external", "Mode"))),
        new XElement("column", new XAttribute("name", "InlineMode"), new XAttribute("type", "INT"),
            new XElement("static_define_list", new XElement("elements", new XAttribute("type", "int"),
                new XElement("element", new XAttribute("name", "None"), 0),
                new XElement("element", new XAttribute("name", "Enabled"), 1)))));
    for (int i = 0; i < 24; i++)
        columns.Add(new XElement("column", new XAttribute("name", "Values" + i), new XAttribute("type", "INTARRAY")));
    new XDocument(new XElement("parse", new XAttribute("name", "Sample"), columns)).Save(Path.Combine(temporary, "Sample.parse.xml"));
    new XDocument(new XElement("enum", new XAttribute("name", "Mode"), new XElement("elements", new XAttribute("type", "int"),
        new XElement("element", new XAttribute("name", "None"), 0),
        new XElement("element", new XAttribute("name", "Enabled"), 1)))).Save(Path.Combine(temporary, "Mode.enum.xml"));
    var generatorType = typeof(ParseLoader).Assembly.GetType("Gibbed.Cryptic.GenerateSerializer.Generator", true);
    var targetGame = generatorType.GetConstructors()[0].GetParameters()[0].ParameterType;
    var generator = Activator.CreateInstance(generatorType, Enum.Parse(targetGame, "StarTrekOnline"), new ParseLoader(temporary), new EnumLoader(temporary));
    var output = Path.Combine(temporary, "PortFixture.dll");
    generatorType.GetMethod("ExportAssembly").Invoke(generator, new object[] { output, "synthetic-port-test" });
    if (!File.Exists(output)) throw new Exception("No persisted serializer assembly");
    var assembly = Assembly.Load(File.ReadAllBytes(output));
    if (assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description != "synthetic-port-test")
        throw new Exception("Serializer version metadata was lost");
    var type = assembly.GetType("Gibbed.StarTrekOnline.Serialization.Sample", true);
    var instance = Activator.CreateInstance(type);
    var construct = type.GetMethod("ConstructLists", BindingFlags.Instance | BindingFlags.NonPublic);
    construct.Invoke(instance, null);
    foreach (var field in type.GetFields().Where(f => f.Name.StartsWith("Values")))
        if (field.GetValue(instance) is not IList) throw new Exception("Generated list was not initialized");
    type.GetField("bf").SetValue(instance, new[] { 3 });
    var usedFields = type.GetProperty("__wrapper_bf").GetValue(instance);
    type.GetProperty("__wrapper_bf").SetValue(instance, usedFields);
    if (((int[])type.GetField("bf").GetValue(instance))[0] != 3) throw new Exception("Field-list wrapper failed");
    type.GetProperty("__wrapper_Mode").SetValue(instance, "Enabled");
    if ((string)type.GetProperty("__wrapper_Mode").GetValue(instance) != "Enabled")
        throw new Exception("Generated enum wrapper did not round trip");
    type.GetProperty("__wrapper_Modes").SetValue(instance, new List<string> { "Enabled", "None" });
    var modes = (List<string>)type.GetProperty("__wrapper_Modes").GetValue(instance);
    if (!modes.SequenceEqual(new[] { "Enabled", "None" })) throw new Exception("Enum list wrapper failed");
    type.GetProperty("__wrapper_InlineMode").SetValue(instance, "Enabled");
    using var serialized = new MemoryStream();
    var serializer = new DataContractSerializer(type);
    serializer.WriteObject(serialized, instance);
    serialized.Position = 0;
    var restored = serializer.ReadObject(serialized);
    if ((string)type.GetProperty("__wrapper_Mode").GetValue(restored) != "Enabled")
        throw new Exception("DataContract serialization did not round trip");
    Console.WriteLine("PASS: persisted assembly, version metadata, external/inline enum wrappers, enum/field lists, 24 list callbacks and DataContract round trip");
}
finally
{
    Directory.Delete(temporary, true);
}
