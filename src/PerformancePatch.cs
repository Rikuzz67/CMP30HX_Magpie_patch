using System;
using System.IO;
using System.Text;

// Exact SAOG0721/Magpie 0.6.11 only. No pattern scanning or unknown binaries.
public static class PerformancePatch {
    public const string CompatHash = "9b49dc3453f0d553a4430ccb6d2ae4fd46a12b67d618a6a2c5463278c8946f5e";
    public const string OutputHash = "949a60edbd59099b035cb6b61ca4eb0d31471ce6a448e46c6a379bb03873b3d6";
    public const string ShaderHash = "e4ebb611c65b63e4890f3665437e4f023dbf4189f6f8dc0b38c5025464df8f0a";
    static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    public static byte[] Apply(byte[] original, byte[] shader) {
        Require(PatchEngine.Hash(original) == CompatHash, "Performance patch requires the exact 0.6.11 compatibility executable");
        Require(PatchEngine.Hash(shader) == ShaderHash, "Performance shader fingerprint mismatch");
        const int start = 4511024, size = 2456, queue = 0x2d1f19;
        Require(shader.Length > 0 && shader.Length <= size && Array.IndexOf(shader, (byte)0) < 0, "Invalid shader allocation");
        byte[] prefix = Encoding.ASCII.GetBytes("\nTexture2D<float4> InputColor");
        for (int i = 0; i < prefix.Length; ++i) Require(original[start+i] == prefix[i], "Original shader mismatch");
        Require(original[start+size] == 0, "Original shader terminator mismatch");
        byte[] expected = new byte[32];
        string hex = "80b8d8010000007414f7806801000000001000750841bd01000000eb0341ffcd";
        for (int i = 0; i < expected.Length; ++i) expected[i] = Convert.ToByte(hex.Substring(i*2,2),16);
        for (int i = 0; i < expected.Length; ++i) Require(original[queue+i] == expected[i], "Original queue instructions mismatch");
        byte[] result = (byte[])original.Clone();
        Array.Clear(result, start, size);
        Array.Copy(shader, 0, result, start, shader.Length);
        byte[] replacement = {0x41,0xbd,0x01,0,0,0};
        Array.Copy(replacement, 0, result, queue, replacement.Length);
        for (int i = replacement.Length; i < 32; ++i) result[queue+i] = 0x90;
        Require(PatchEngine.Hash(result) == OutputHash, "Optimized executable fingerprint mismatch");
        return result;
    }
}
