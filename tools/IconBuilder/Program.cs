if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: IconBuilder OUTPUT.ico SIZE=INPUT.png [SIZE=INPUT.png ...]");
    return 1;
}

var images = args.Skip(1).Select(argument =>
{
    string[] parts = argument.Split('=', 2);
    if (parts.Length != 2 || !int.TryParse(parts[0], out int size) || size is < 1 or > 256)
        throw new ArgumentException($"Invalid image argument: {argument}");
    return (Size: size, Bytes: File.ReadAllBytes(parts[1]));
}).OrderBy(image => image.Size).ToArray();

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
using var output = File.Create(args[0]);
using var writer = new BinaryWriter(output);

writer.Write((ushort)0); // reserved
writer.Write((ushort)1); // icon
writer.Write((ushort)images.Length);

int offset = 6 + images.Length * 16;
foreach (var image in images)
{
    writer.Write((byte)(image.Size == 256 ? 0 : image.Size));
    writer.Write((byte)(image.Size == 256 ? 0 : image.Size));
    writer.Write((byte)0); // palette
    writer.Write((byte)0); // reserved
    writer.Write((ushort)1); // planes
    writer.Write((ushort)32); // bits per pixel
    writer.Write(image.Bytes.Length);
    writer.Write(offset);
    offset += image.Bytes.Length;
}

foreach (var image in images) writer.Write(image.Bytes);
Console.WriteLine($"Created {args[0]} with {images.Length} PNG sizes.");
return 0;
