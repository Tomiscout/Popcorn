using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics;
using NAudio.Wave;
using VideoBlazor.Shared.Utils;
using System.Diagnostics;
using System.Runtime.Serialization.Formatters.Binary;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Strides;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using System.IO;
using SoundFingerprinting.Query;

namespace VideoBlazor.SegmentFinder;

public static class SegmentFinder
{
    public const double SLIVER_DURATION = 1.0 / 20.0;
    public const int FFT_SIZE = 2048;
    public static readonly int BIN_COUNT = FFT_SIZE / 2;

    public static readonly int[] FREQUENCY_BANDS = {
            0,
            100,
            200,
            400,
            800,
            1600,
            8000
        };

    private static string WorkDirectory = "";
    public static void Initialize()
    {
        WorkDirectory = Path.Combine(DirUtils.DataFolder, "segmentFinder\\");
        Directory.CreateDirectory(WorkDirectory);
    }

    public static async Task Test()
    {
        try
        {
            var jingleLocations = await PatternFinder.FindMatchingAudio(@"C:\Users\brazi\Music\office1.wav", @"C:\Users\brazi\Music\office2.wav");

        }
        catch (Exception e)
        {

        }
    }

    


public static void SaveToBinaryFile(float[][] data, string filePath)
{
    using (var binWriter = new BinaryWriter(File.Open(filePath, FileMode.Create)))
    {
        foreach (var array in data)
        {
            // Write the length of the array first
            binWriter.Write(array.Length);
            foreach (var number in array)
            {
                // Write each float to the file
                binWriter.Write(number);
            }
        }
    }
}


public static float[][] ReadFromBinaryFile(string filePath)
{
    using (var binReader = new BinaryReader(File.Open(filePath, FileMode.Open)))
    {
        var listOfArrays = new List<float[]>();
        while (binReader.BaseStream.Position != binReader.BaseStream.Length)
        {
            // Read the length of the array
            int arrayLength = binReader.ReadInt32();
            var array = new float[arrayLength];
            for (int i = 0; i < arrayLength; i++)
            {
                // Read each float from the file
                array[i] = binReader.ReadSingle();
            }
            listOfArrays.Add(array);
        }
        return listOfArrays.ToArray();
    }
}


}
