using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace BodyDataTool;

/// <summary>Triangle mesh as stored in a glTF primitive (positions and triangle indices).</summary>
public sealed record TriangleMesh(Vector3[] Positions, int[] Indices)
{
    public int TriangleCount => Indices.Length / 3;

    public Vector3 Centroid(int triangle) =>
        (Positions[Indices[triangle * 3]] + Positions[Indices[triangle * 3 + 1]] + Positions[Indices[triangle * 3 + 2]]) / 3f;
}

/// <summary>
/// Minimal reader for binary glTF 2.0 (.glb): extracts one named mesh with a single triangle primitive,
/// exactly as three.js sees it (index order = faceIndex order).
/// </summary>
public static class GlbReader
{
    private const uint GlbMagic = 0x46546C67; // "glTF"
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinChunk = 0x004E4942;

    public static TriangleMesh ReadMesh(string path, string meshName)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != GlbMagic)
            throw new InvalidDataException($"{path} is not a binary glTF file.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != 2)
            throw new InvalidDataException("Only glTF 2.0 is supported.");

        ReadOnlyMemory<byte> json = default, bin = default;
        var offset = 12;
        while (offset + 8 <= data.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            var type = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            var chunk = data.AsMemory(offset + 8, length);
            if (type == JsonChunk) json = chunk;
            else if (type == BinChunk) bin = chunk;
            offset += 8 + length;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var meshes = root.GetProperty("meshes");
        var meshIndex = -1;
        for (var i = 0; i < meshes.GetArrayLength(); i++)
            if (meshes[i].TryGetProperty("name", out var n) && n.GetString() == meshName) meshIndex = i;
        if (meshIndex < 0) throw new InvalidDataException($"Mesh '{meshName}' not found in {path}.");

        EnsureIdentityNode(root, meshIndex, meshName);

        var primitives = meshes[meshIndex].GetProperty("primitives");
        if (primitives.GetArrayLength() != 1)
            throw new InvalidDataException($"Mesh '{meshName}' must have exactly one primitive (has {primitives.GetArrayLength()}).");
        var primitive = primitives[0];
        if (primitive.TryGetProperty("mode", out var mode) && mode.GetInt32() != 4)
            throw new InvalidDataException("Only triangle primitives are supported.");

        var positions = ReadVec3(root, bin.Span, primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32());
        var indices = primitive.TryGetProperty("indices", out var idx)
            ? ReadIndices(root, bin.Span, idx.GetInt32())
            : Enumerable.Range(0, positions.Length).ToArray();
        if (indices.Length % 3 != 0) throw new InvalidDataException("Index count is not a multiple of 3.");
        return new TriangleMesh(positions, indices);
    }

    private static void EnsureIdentityNode(JsonElement root, int meshIndex, string meshName)
    {
        foreach (var node in root.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("mesh", out var m) || m.GetInt32() != meshIndex) continue;
            var hasTransform = node.TryGetProperty("matrix", out _) || NotDefault(node, "translation", 0) ||
                               NotDefault(node, "scale", 1) || NotDefault(node, "rotation", 0, w: 1);
            if (hasTransform)
                throw new InvalidDataException($"Node of mesh '{meshName}' has a transform; apply transforms before export.");
        }
    }

    private static bool NotDefault(JsonElement node, string name, double value, double? w = null)
    {
        if (!node.TryGetProperty(name, out var array)) return false;
        var i = 0;
        foreach (var item in array.EnumerateArray())
        {
            var expected = w.HasValue && i == 3 ? w.Value : value;
            if (Math.Abs(item.GetDouble() - expected) > 1e-6) return true;
            i++;
        }

        return false;
    }

    private static (int Offset, int Count, int Stride, int ComponentType, string Type) Accessor(JsonElement root, int index)
    {
        var accessor = root.GetProperty("accessors")[index];
        var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        var offset = (view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0) +
                     (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
        var stride = view.TryGetProperty("byteStride", out var s) ? s.GetInt32() : 0;
        return (offset, accessor.GetProperty("count").GetInt32(), stride, accessor.GetProperty("componentType").GetInt32(),
            accessor.GetProperty("type").GetString()!);
    }

    private static Vector3[] ReadVec3(JsonElement root, ReadOnlySpan<byte> bin, int accessorIndex)
    {
        var (offset, count, stride, componentType, type) = Accessor(root, accessorIndex);
        if (componentType != 5126 || type != "VEC3") throw new InvalidDataException("POSITION must be float VEC3.");
        stride = stride == 0 ? 12 : stride;
        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            var p = bin.Slice(offset + i * stride, 12);
            result[i] = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(p), BinaryPrimitives.ReadSingleLittleEndian(p[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(p[8..]));
        }

        return result;
    }

    private static int[] ReadIndices(JsonElement root, ReadOnlySpan<byte> bin, int accessorIndex)
    {
        var (offset, count, stride, componentType, type) = Accessor(root, accessorIndex);
        if (type != "SCALAR") throw new InvalidDataException("Indices must be SCALAR.");
        var size = componentType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw new InvalidDataException("Bad index type.") };
        stride = stride == 0 ? size : stride;
        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            var p = bin.Slice(offset + i * stride, size);
            result[i] = size switch
            {
                1 => p[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(p),
                _ => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(p)),
            };
        }

        return result;
    }
}

public static class ObjReader
{
    /// <summary>Reads 'v' and triangular 'f' records of a Wavefront OBJ file.</summary>
    public static TriangleMesh Read(string path)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                positions.Add(new Vector3(float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture)));
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length != 4) throw new InvalidDataException($"Only triangles are supported: '{line}'.");
                for (var k = 1; k <= 3; k++) indices.Add(int.Parse(p[k].Split('/')[0]) - 1);
            }
        }

        return new TriangleMesh(positions.ToArray(), indices.ToArray());
    }
}
