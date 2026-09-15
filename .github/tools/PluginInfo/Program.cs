// Prints one JSON array describing every BepInEx plugin DLL in a directory:
//
//   [{ "file": "Foo.dll", "guid": "...", "name": "...", "version": "1.2.3",
//      "dependencies": ["other.plugin.guid", ...] }, ...]
//
// The version is the BepInPlugin attribute's third argument - what BepInEx logs
// and what Thunderstore should show - rather than the assembly version, which
// not every project in this repo keeps in step with it.
//
// Usage: dotnet run --project .github/tools/PluginInfo -- <dir-or-dll> [...]

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

var paths = args.Length > 0 ? args : throw new ArgumentException("Pass a directory or one or more DLL paths.");

IEnumerable<string> files = paths.SelectMany<string, string>(p =>
    Directory.Exists(p) ? Directory.EnumerateFiles(p, "*.dll").OrderBy(f => f, StringComparer.Ordinal) : [p]);

var plugins = new List<object>();
foreach (string file in files)
{
    var info = ReadPlugin(file);
    if (info is not null)
        plugins.Add(info);
}

Console.WriteLine(JsonSerializer.Serialize(plugins, new JsonSerializerOptions { WriteIndented = true }));
return 0;

static object? ReadPlugin(string file)
{
    using var stream = File.OpenRead(file);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata)
        return null;

    MetadataReader md = pe.GetMetadataReader();

    foreach (TypeDefinitionHandle typeHandle in md.TypeDefinitions)
    {
        TypeDefinition type = md.GetTypeDefinition(typeHandle);
        string? guid = null, name = null, version = null;
        var dependencies = new List<string>();

        foreach (CustomAttributeHandle attrHandle in type.GetCustomAttributes())
        {
            CustomAttribute attr = md.GetCustomAttribute(attrHandle);
            string attrType = AttributeTypeName(md, attr);

            if (attrType == "BepInPlugin")
            {
                BlobReader blob = md.GetBlobReader(attr.Value);
                blob.ReadUInt16(); // prolog
                guid = blob.ReadSerializedString();
                name = blob.ReadSerializedString();
                version = blob.ReadSerializedString();
            }
            else if (attrType == "BepInDependency")
            {
                // Both constructors take the dependency GUID first.
                BlobReader blob = md.GetBlobReader(attr.Value);
                blob.ReadUInt16();
                string? dep = blob.ReadSerializedString();
                if (dep is not null)
                    dependencies.Add(dep);
            }
        }

        if (guid is not null)
            return new { file = Path.GetFileName(file), guid, name, version, dependencies };
    }

    return null;
}

static string AttributeTypeName(MetadataReader md, CustomAttribute attr)
{
    switch (attr.Constructor.Kind)
    {
        case HandleKind.MemberReference:
        {
            MemberReference ctor = md.GetMemberReference((MemberReferenceHandle)attr.Constructor);
            if (ctor.Parent.Kind == HandleKind.TypeReference)
                return md.GetString(md.GetTypeReference((TypeReferenceHandle)ctor.Parent).Name);
            if (ctor.Parent.Kind == HandleKind.TypeDefinition)
                return md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)ctor.Parent).Name);
            return string.Empty;
        }
        case HandleKind.MethodDefinition:
        {
            MethodDefinition ctor = md.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
            return md.GetString(md.GetTypeDefinition(ctor.GetDeclaringType()).Name);
        }
        default:
            return string.Empty;
    }
}
