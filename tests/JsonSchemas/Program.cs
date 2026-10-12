using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Gibbed.Cryptic.FileFormats;
using Gibbed.Cryptic.FileFormats.ParseSchema;
using Gibbed.Cryptic.GenerateSerializer;

object Canonical(object value)
{
    if (value is null || value is string || value.GetType().IsPrimitive) return value;
    if (value is Enum) return value.ToString();
    if (value is IDictionary dictionary)
    {
        var values = new SortedDictionary<string, object>();
        foreach (DictionaryEntry entry in dictionary) values.Add(entry.Key.ToString(), Canonical(entry.Value));
        return values;
    }
    if (value is IEnumerable items) return items.Cast<object>().Select(Canonical).ToArray();
    var result = new SortedDictionary<string, object>();
    foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
    {
        if (value is Column column && field.Name == "Subtable" && column.SubtableIsExternal) continue;
        result.Add(field.Name, Canonical(field.GetValue(value)));
    }
    return result;
}

void Equal(object first, object second, string name)
{
    if (JsonSerializer.Serialize(Canonical(first)) != JsonSerializer.Serialize(Canonical(second)))
        throw new Exception("Schema representations differ: " + name);
}

if (args.Length == 2)
{
    var json = new JsonSchemaBundle(args[0]);
    var parses = new ParseLoader(args[1]);
    var enums = new EnumLoader(args[1]);
    if (!json.Parses.ParseNames.Order().SequenceEqual(parses.ParseNames.Order()) || !json.Enums.EnumNames.Order().SequenceEqual(enums.EnumNames.Order()))
        throw new Exception("Schema membership differs");
    foreach (var name in parses.ParseNames) Equal(json.Parses.LoadParse(name), parses.LoadParse(name), name);
    foreach (var name in enums.EnumNames) Equal(json.Enums.LoadEnum(name), enums.LoadEnum(name), name);
    Console.WriteLine($"PASS: {parses.ParseNames.Count()} parses and {enums.EnumNames.Count()} enums are semantically identical between JSON and XML");
    return;
}

object Node(XElement element) => new Dictionary<string, object> {
    ["tag"] = element.Name.LocalName,
    ["attributes"] = element.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value),
    ["text"] = element.HasElements ? "" : element.Value,
    ["children"] = element.Elements().Select(Node).ToArray()
};

var temporary = Path.Combine(Path.GetTempPath(), "gibbed-json-schemas-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var schema = new XElement("parse", new XAttribute("name", "Fixture"), new XElement("table",
        new XElement("column", new XAttribute("name", "Name"), new XAttribute("type", "STRING"), new XElement("default_string", " preserve spaces ")),
        new XElement("column", new XAttribute("name", "Values"), new XAttribute("type", "INTARRAY")),
        new XElement("column", new XAttribute("name", "Mode"), new XAttribute("type", "INT"), new XElement("static_define_list", new XAttribute("external", "Mode"))),
        new XElement("column", new XAttribute("name", "Child"), new XAttribute("type", "OPTIONALSTRUCT"), new XElement("subtable", new XAttribute("external", "Child")))));
    var child = new XElement("parse", new XAttribute("name", "Child"), new XElement("table", new XElement("column", new XAttribute("name", "Number"), new XAttribute("type", "INT"))));
    var enumeration = new XElement("enum", new XAttribute("name", "Mode"), new XElement("elements", new XAttribute("type", "int"),
        new XElement("element", new XAttribute("name", "None"), 0), new XElement("element", new XAttribute("name", "Enabled"), 1)));
    schema.Save(Path.Combine(temporary, "Fixture.parse.xml"));
    child.Save(Path.Combine(temporary, "Child.parse.xml"));
    enumeration.Save(Path.Combine(temporary, "Mode.enum.xml"));
    var model = new Dictionary<string, object> {
        ["format"] = "stodex-schema-tree", ["version"] = 1, ["representation"] = "bin-schema-metadata",
        ["game_build"] = "ST.1.20000101a.1", ["schema_id"] = "fixture",
        ["parses"] = new Dictionary<string, object> { ["Fixture.parse.xml"] = Node(schema), ["Child.parse.xml"] = Node(child) },
        ["enums"] = new Dictionary<string, object> { ["Mode.enum.xml"] = Node(enumeration) }
    };
    var path = Path.Combine(temporary, "schemas.json");
    File.WriteAllText(path, JsonSerializer.Serialize(model));
    var json = new JsonSchemaBundle(path);
    Equal(json.Parses.LoadParse("Fixture"), new ParseLoader(temporary).LoadParse("Fixture"), "Fixture");
    Equal(json.Enums.LoadEnum("Mode"), new EnumLoader(temporary).LoadEnum("Mode"), "Mode");
    var generatorType = typeof(ParseLoader).Assembly.GetType("Gibbed.Cryptic.GenerateSerializer.Generator", true);
    var targetGame = generatorType.GetConstructors()[0].GetParameters()[0].ParameterType;
    var generator = Activator.CreateInstance(generatorType, Enum.Parse(targetGame, "StarTrekOnline"), json.Parses, json.Enums);
    var output = Path.Combine(temporary, "JsonFixture.dll");
    generatorType.GetMethod("ExportAssembly").Invoke(generator, new object[] { output, "json-fixture" });
    var assembly = Assembly.Load(File.ReadAllBytes(output));
    var type = assembly.GetType("Gibbed.StarTrekOnline.Serialization.Fixture", true);
    var instance = Activator.CreateInstance(type);
    type.GetMethod("ConstructLists", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
    if (type.GetField("Values").GetValue(instance) is not IList) throw new Exception("JSON-derived lists are uninitialized");
    type.GetProperty("__wrapper_Mode").SetValue(instance, "Enabled");
    if ((string)type.GetProperty("__wrapper_Mode").GetValue(instance) != "Enabled") throw new Exception("JSON-derived enum wrapper failed");
    model["representation"] = "native-memory-layout";
    File.WriteAllText(path, JsonSerializer.Serialize(model));
    try { new JsonSchemaBundle(path); throw new Exception("Memory offsets were accepted as BIN schemas"); }
    catch (FormatException) { }
    model["representation"] = "bin-schema-metadata";
    model["version"] = 2;
    File.WriteAllText(path, JsonSerializer.Serialize(model));
    try { new JsonSchemaBundle(path); throw new Exception("Unsupported version was accepted"); }
    catch (FormatException) { }
    Console.WriteLine("PASS: JSON/XML equality, external schemas, enums, literal whitespace, generated list/enum IL, and invalid format rejection");
}
finally
{
    Directory.Delete(temporary, true);
}
