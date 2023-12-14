using System;
using System.IO;
using System.Globalization;
using UnityEngine;

#if UNITY_2020_1_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

[ScriptedImporter(1, "spi1d")]
public class Spi1DImporter : ScriptedImporter
{
    public bool convertLogToLinearPre = true;
    public bool convertLinearToLogPre = false;
    public bool convertLogToLinearPost = false;
    public bool convertLinearToLogPost = false;

    public int overrideResolution = 0;

    // Initialize variables for LUT data
    [ReadOnly] 
    private float[] lutData;
    [SerializeField] [ReadOnly]
    private float minValue;
    [SerializeField] [ReadOnly]
    private float maxValue;
    [SerializeField] [ReadOnly]
    private int length;

    // Helper for transforming data for Unity
    class LogColorTransform
    {
        struct ParamsLogC
        {
            public float cut;
            public float a, b, c, d, e, f;
        }

        static readonly ParamsLogC LogC = new ParamsLogC
        {
            cut = 0.011361f, // cut
            a = 5.555556f, // a
            b = 0.047996f, // b
            c = 0.244161f, // c
            d = 0.386036f, // d
            e = 5.301883f, // e
            f = 0.092819f  // f
        };

        public float LinearToLogC_Precise(float x)
        {
            float o;
            if (x > LogC.cut)
                o = LogC.c * Mathf.Log10(LogC.a * x + LogC.b) + LogC.d;
            else
                o = LogC.e * x + LogC.f;
            return o;
        }

        public Vector3 LinearToLogC(Vector3 x)
        {
        #if USE_PRECISE_LOGC
            return new Vector3(
                LinearToLogC_Precise(x.x),
                LinearToLogC_Precise(x.y),
                LinearToLogC_Precise(x.z)
            );
        #else
            //return LogC.c * Mathf.Log10(LogC.a * x + LogC.b) + LogC.d;
            return new Vector3(
                LogC.c * Mathf.Log10(LogC.a * x.x + LogC.b) + LogC.d,
                LogC.c * Mathf.Log10(LogC.a * x.y + LogC.b) + LogC.d,
                LogC.c * Mathf.Log10(LogC.a * x.z + LogC.b) + LogC.d
            );
        #endif
        }

        public float LogCToLinear_Precise(float x)
        {
            float o;
            if (x > LogC.e * LogC.cut + LogC.f)
                o = (Mathf.Pow(10.0f, (x - LogC.d) / LogC.c) - LogC.b) / LogC.a;
            else
                o = (x - LogC.f) / LogC.e;
            return o;
        }

        public Vector3 LogCToLinear(Vector3 x)
        {
        #if USE_PRECISE_LOGC
            return new Vector3(
                LogCToLinear_Precise(x.x),
                LogCToLinear_Precise(x.y),
                LogCToLinear_Precise(x.z)
            );
        #else
            // return (Mathf.Pow(10.0f, (x - LogC.d) / LogC.c) - LogC.b) / LogC.a;
            return new Vector3(
                (Mathf.Pow(10.0f, (x.x- LogC.d) / LogC.c) - LogC.b) / LogC.a,
                (Mathf.Pow(10.0f, (x.y- LogC.d) / LogC.c) - LogC.b) / LogC.a,
                (Mathf.Pow(10.0f, (x.z- LogC.d) / LogC.c) - LogC.b) / LogC.a
            );
        #endif
        }

    }

    void Import(string filePath)
    {
        // Read all lines from the file
        string[] lines = File.ReadAllLines(filePath);

        // Parse the file line by line
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            string[] tokens = line.Split(' ');

            if (tokens[0] == "From")
            {
                minValue = float.Parse(tokens[1], CultureInfo.InvariantCulture);
                maxValue = float.Parse(tokens[2], CultureInfo.InvariantCulture);
            }
            else if (tokens[0] == "Length")
            {
                length = int.Parse(tokens[1], CultureInfo.InvariantCulture);
                lutData = new float[length];
            }
            else if (tokens[0] == "{")
            {
                for (int j = 0; j < length; j++)
                {
                    lutData[j] = float.Parse(lines[i + j + 1], CultureInfo.InvariantCulture);
                }
                break;
            }
        }
    }

    public float MapValue(float input)
    {
        // Normalize the input value to the range [0, 1]
        float normalizedInput = (input - minValue) / (maxValue - minValue);

        // Find the index of the LUT entry that corresponds to the normalized input value
        int index = (int)(normalizedInput * (length - 1));

        // Clamp the index to the valid range
        index = Math.Max(Math.Min(index, length - 1), 0);

        // Return the LUT entry
        return lutData[index];
    }

    Vector3 ColorToVector3(Color color)
    {
        return new Vector3(color.r, color.g, color.b);
    }

    Color Vector3ToColor(Vector3 vector)
    {
        return new Color(vector.x, vector.y, vector.z);
    }

    Color ApplyGamma(Color color, float gamma)
	{
    float invGamma = 1.0f / gamma;
    return new Color(
        Mathf.Pow(color.r, invGamma),
        Mathf.Pow(color.g, invGamma),
        Mathf.Pow(color.b, invGamma)
    );
	}
	

    // Use the imported LUT data as needed
    public override void OnImportAsset(AssetImportContext ctx)
    {
        // Read the texture data from the file
        Import(ctx.assetPath);

        int size = overrideResolution > 0 ? overrideResolution : 33;

        // Create a new 3D texture
        Texture3D texture = new Texture3D(size, size, size, TextureFormat.RGBAHalf, false)
            {
                anisoLevel = 0,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        
        // Set the pixel data for the texture
        Color[] colors = new Color[size * size * size];
        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = (float)x / (size - 1);
                    float g = (float)y / (size - 1);
                    float b = (float)z / (size - 1);

                    LogColorTransform lct = new LogColorTransform();
                    Color thisCol = new Color(r, g, b);

                    if (convertLinearToLogPre)
                    {
                        thisCol.r = lct.LinearToLogC_Precise(thisCol.r);
                        thisCol.g = lct.LinearToLogC_Precise(thisCol.g);
                        thisCol.b = lct.LinearToLogC_Precise(thisCol.b);
                    }

                    if (convertLogToLinearPre)
                    {
                        thisCol.r = lct.LogCToLinear_Precise(thisCol.r);
                        thisCol.g = lct.LogCToLinear_Precise(thisCol.g);
                        thisCol.b = lct.LogCToLinear_Precise(thisCol.b);
                    }

                    thisCol.r = MapValue(thisCol.r);
                    thisCol.g = MapValue(thisCol.g);
                    thisCol.b = MapValue(thisCol.b);
                    
                    if (convertLinearToLogPost)
                    {
                        thisCol.r = lct.LinearToLogC_Precise(thisCol.r);
                        thisCol.g = lct.LinearToLogC_Precise(thisCol.g);
                        thisCol.b = lct.LinearToLogC_Precise(thisCol.b);
                    } 

                    if (convertLogToLinearPost)
                    {
                        thisCol.r = lct.LogCToLinear_Precise(thisCol.r);
                        thisCol.g = lct.LogCToLinear_Precise(thisCol.g);
                        thisCol.b = lct.LogCToLinear_Precise(thisCol.b);
                    }

                    colors[x + y * size + z * size * size] = thisCol;
                }
            }
        }
        texture.SetPixels(colors, 0);
        texture.Apply();

        // Add the texture to the asset import context
        ctx.AddObjectToAsset("main tex", texture);
        ctx.SetMainObject(texture);
    }
}
