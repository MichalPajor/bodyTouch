using System.Numerics;
using System.Runtime.InteropServices;

namespace BodyTouchGame.Geometry;

/// <summary>
/// Binary file format of <see cref="BodyGraph"/> (body_graph.bin). Little-endian, described in
/// docs/formaty/body_graph.md. Layout:
/// <code>
/// uint32 magic "BTGG" | int32 version | int32 graphVertexCount N | int32 edgeCount M
/// int32 meshVertexCount V | int32 meshTriangleCount T
/// float32[3N] graph positions | int32[N+1] row offsets | int32[M] neighbours | float32[M] edge lengths
/// float32[3V] mesh positions  | int32[3T] mesh indices | int32[3T] triangle anchors | int32[N] vertex triangles
/// </code>
/// </summary>
public static class BodyGraphSerializer
{
    public const uint Magic = 0x47475442; // "BTGG" read as little-endian uint32
    public const int FormatVersion = 1;

    private const int MaxGraphVertices = 1_000_000;
    private const int MaxEdges = 64_000_000;
    private const int MaxMeshVertices = 4_000_000;
    private const int MaxMeshTriangles = 8_000_000;

    public static void Write(BodyGraph body, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(stream);
        EnsureLittleEndian();

        var graph = body.Graph;
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(graph.VertexCount);
        writer.Write(graph.EdgeCount);
        writer.Write(body.MeshVertexCount);
        writer.Write(body.TriangleCount);
        writer.Flush();

        WriteArray(stream, graph.Positions);
        WriteArray(stream, graph.RowOffsets);
        WriteArray(stream, graph.Neighbors);
        WriteArray(stream, graph.EdgeLengths);
        WriteArray(stream, body.MeshPositions);
        WriteArray(stream, body.MeshIndices);
        WriteArray(stream, body.TriangleAnchors);
        WriteArray(stream, body.VertexTriangles);
    }

    public static void WriteFile(BodyGraph body, string path)
    {
        using var stream = File.Create(path);
        Write(body, stream);
    }

    /// <summary>Reads and validates a body graph. Works with non-seekable streams (e.g. app package assets).</summary>
    /// <exception cref="InvalidDataException">The data is not a valid body graph file.</exception>
    public static BodyGraph Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        EnsureLittleEndian();

        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Not a body graph file (bad magic number).");
            var version = reader.ReadInt32();
            if (version != FormatVersion) throw new InvalidDataException($"Unsupported body graph version {version}.");

            var vertexCount = ReadCount(reader, MaxGraphVertices, "graph vertex count");
            var edgeCount = ReadCount(reader, MaxEdges, "edge count");
            var meshVertexCount = ReadCount(reader, MaxMeshVertices, "mesh vertex count");
            var triangleCount = ReadCount(reader, MaxMeshTriangles, "mesh triangle count");

            var positions = ReadArray<Vector3>(stream, vertexCount);
            var rowOffsets = ReadArray<int>(stream, vertexCount + 1);
            var neighbors = ReadArray<int>(stream, edgeCount);
            var lengths = ReadArray<float>(stream, edgeCount);
            var meshPositions = ReadArray<Vector3>(stream, meshVertexCount);
            var meshIndices = ReadArray<int>(stream, triangleCount * 3);
            var anchors = ReadArray<int>(stream, triangleCount * BodyGraph.AnchorsPerTriangle);
            var vertexTriangles = ReadArray<int>(stream, vertexCount);

            var graph = new SurfaceGraph(positions, rowOffsets, neighbors, lengths);
            return new BodyGraph(graph, meshPositions, meshIndices, anchors, vertexTriangles);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("Body graph file is truncated.", e);
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException($"Body graph file is inconsistent: {e.Message}", e);
        }
    }

    public static BodyGraph ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    private static int ReadCount(BinaryReader reader, int max, string name)
    {
        var value = reader.ReadInt32();
        if (value < 0 || value > max) throw new InvalidDataException($"Invalid {name}: {value}.");
        return value;
    }

    private static void WriteArray<T>(Stream stream, ReadOnlySpan<T> data) where T : unmanaged =>
        stream.Write(MemoryMarshal.AsBytes(data));

    private static T[] ReadArray<T>(Stream stream, int count) where T : unmanaged
    {
        var array = new T[count];
        stream.ReadExactly(MemoryMarshal.AsBytes(array.AsSpan()));
        return array;
    }

    private static void EnsureLittleEndian()
    {
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("Body graph files require a little-endian platform.");
    }
}
