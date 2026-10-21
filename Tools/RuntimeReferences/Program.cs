using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Read the embedded provider as metadata only; do not execute third-party code.
if (args.Length != 2) throw new ArgumentException("Usage: RuntimeReferences <RimUnity.Embedded.dll> <output>");
using var input = File.OpenRead(args[0]);
using var pe = new PEReader(input);
var metadata = pe.GetMetadataReader();
var resource = metadata.ManifestResources.Select(metadata.GetManifestResource)
    .Single(r => metadata.GetString(r.Name) == "RimUnity.Payload.zip");
if (!resource.Implementation.IsNil) throw new InvalidDataException("Expected an embedded payload.");
var bytes = pe.GetSectionData(pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress)
    .GetReader((int)resource.Offset, pe.PEHeaders.CorHeader.ResourcesDirectory.Size - (int)resource.Offset);
using var payload = new MemoryStream(bytes.ReadBytes(bytes.ReadInt32()));
using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
foreach (var entry in zip.Entries)
{
    var name = entry.FullName.Replace('\\', '/');
    bool reference = name == "Assemblies/RimUnity.dll" || name == "Assemblies/RimUnity.Core.dll" || name == "Assemblies/RimUnity.Spine.dll";
    bool license = name == "LICENSE" || name.StartsWith("ThirdParty/", StringComparison.Ordinal);
    if (entry.Name.Length == 0 || (!reference && !license && name != "runtime.json")) continue;
    var target = Path.GetFullPath(Path.Combine(output, name));
    if (!target.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Unsafe payload entry: " + name);
    Directory.CreateDirectory(Path.GetDirectoryName(target));
    entry.ExtractToFile(target, true);
}
Console.WriteLine(File.ReadAllText(Path.Combine(output, "runtime.json")));
