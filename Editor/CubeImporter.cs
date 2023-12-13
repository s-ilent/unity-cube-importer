using System.IO;
using UnityEngine;
using System.Globalization;
#if UNITY_2020_1_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

[ScriptedImporter(1, "cube")]
public class CubeImporter : ScriptedImporter
{
    public bool convertLogToLinearPre = true;
    public bool convertLinearToLogPre = false;
    public bool convertLogToLinearPost = false;
    public bool convertLinearToLogPost = false;

    public int overrideResolution = 0;

    // Initialize variables for LUT data
    [SerializeField] [ReadOnly] [Space]
    private int lut3DSize = 0;
    [SerializeField] [ReadOnly]
    private float lut3DInputRangeMin = 0;
    [SerializeField] [ReadOnly]
    private float lut3DInputRangeMax = 1;
    private Color[] lut3DData = null;

    [SerializeField] [ReadOnly]
    private int lut1DSize = 0;
    [SerializeField] [ReadOnly]
    private float lut1DInputRangeMin = 0;
    [SerializeField] [ReadOnly]
    private float lut1DInputRangeMax = 1;
    private Color[] lut1DData = null;

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
        int lineIndex = 0;
        while (lineIndex < lines.Length)
        {
            string line = lines[lineIndex].Trim();
            string[] tokens = line.Split(' ');
            bool hasParsed1D = false;
            int index1D = 0;
            bool hasParsed3D = false;
            int index3D = 0;

            if (line.StartsWith("#"))
            {
                // Comment line, skip
            }
            else if (line.StartsWith("LUT_3D_SIZE"))
            {
                // Parse 3D LUT size
                lut3DSize = int.Parse(tokens[1], CultureInfo.InvariantCulture);
                lut3DData = new Color[lut3DSize * lut3DSize * lut3DSize];
            }
            else if (line.StartsWith("LUT_3D_INPUT_RANGE"))
            {
                // Parse 3D LUT input range
                lut3DInputRangeMin = float.Parse(tokens[1], CultureInfo.InvariantCulture);
                lut3DInputRangeMax = float.Parse(tokens[2], CultureInfo.InvariantCulture);
            }
            else if (line.StartsWith("LUT_1D_SIZE"))
            {
                // Parse 1D LUT size
                lut1DSize = int.Parse(tokens[1], CultureInfo.InvariantCulture);
                lut1DData = new Color[lut1DSize];
            }
            else if (line.StartsWith("LUT_1D_INPUT_RANGE"))
            {
                // Parse 1D LUT input range
                lut1DInputRangeMin = float.Parse(tokens[1], CultureInfo.InvariantCulture);
                lut1DInputRangeMax = float.Parse(tokens[2], CultureInfo.InvariantCulture);
            }
            else if (lut1DData != null && !hasParsed1D)
            {
                // Parse 1D LUT data
                float r = float.Parse(tokens[0], CultureInfo.InvariantCulture);
                float g = float.Parse(tokens[1], CultureInfo.InvariantCulture);
                float b = float.Parse(tokens[2], CultureInfo.InvariantCulture);
                lut1DData[index1D] = new Color(r, g, b);
                index1D++;
                if (index1D >= lut1DSize) hasParsed1D = true;
            }
            else if (lut3DData != null && !hasParsed3D)
            {
                // Parse 3D LUT data
                float r = float.Parse(tokens[0], CultureInfo.InvariantCulture);
                float g = float.Parse(tokens[1], CultureInfo.InvariantCulture);
                float b = float.Parse(tokens[2], CultureInfo.InvariantCulture);
                lut3DData[index3D] = new Color(r, g, b);
                index3D++;
                if (index3D >= lut3DSize) hasParsed3D = true;
            }

            lineIndex++;
        }
    }

    Color Map1D(Color input)
    {
        // Normalize the input value to the range [0, 1]
        float normalizedInput = (input.r - lut1DInputRangeMin) / (lut1DInputRangeMax - lut1DInputRangeMin);

        // Find the index of the LUT entry that corresponds to the normalized input value
        int index = Mathf.Clamp(Mathf.FloorToInt(normalizedInput * (lut1DData.Length - 1)), 0, lut1DData.Length - 2);

        // Find the two LUT entries that the input value falls between
        Color lower = lut1DData[index];
        Color upper = lut1DData[index + 1];

        // Interpolate between the two LUT entries to find the remapped value
        float t = normalizedInput * (lut1DData.Length - 1) - index;
        Color result = Color.Lerp(lower, upper, t);

        return result;
    }

    Color Map3D(Color input)
    {
        // Normalize the input value to the range [0, 1]
        Vector3 normalizedInput = new Vector3(
            (input.r - lut3DInputRangeMin) / (lut3DInputRangeMax - lut3DInputRangeMin),
            (input.g - lut3DInputRangeMin) / (lut3DInputRangeMax - lut3DInputRangeMin),
            (input.b - lut3DInputRangeMin) / (lut3DInputRangeMax - lut3DInputRangeMin));

        // Find the indices of the LUT entries that correspond to the normalized input value
        Vector3 indices = normalizedInput * (lut3DSize - 1);
        Vector3Int lowerIndices = Vector3Int.FloorToInt(indices);
        Vector3Int upperIndices = Vector3Int.CeilToInt(indices);

        // Clamp the indices to the valid range
        lowerIndices = Vector3Int.Max(Vector3Int.Min(lowerIndices, new Vector3Int(lut3DSize - 2, lut3DSize - 2, lut3DSize - 2)), Vector3Int.zero);
        upperIndices = Vector3Int.Max(Vector3Int.Min(upperIndices, new Vector3Int(lut3DSize - 1, lut3DSize - 1, lut3DSize - 1)), Vector3Int.one);

        // Find the eight LUT entries that the input value falls between
        Color c000 = lut3DData[lowerIndices.x + lowerIndices.y * lut3DSize + lowerIndices.z * lut3DSize * lut3DSize];
        Color c001 = lut3DData[lowerIndices.x + lowerIndices.y * lut3DSize + upperIndices.z * lut3DSize * lut3DSize];
        Color c010 = lut3DData[lowerIndices.x + upperIndices.y * lut3DSize + lowerIndices.z * lut3DSize * lut3DSize];
        Color c011 = lut3DData[lowerIndices.x + upperIndices.y * lut3DSize + upperIndices.z * lut3DSize * lut3DSize];
        Color c100 = lut3DData[upperIndices.x + lowerIndices.y * lut3DSize + lowerIndices.z * lut3DSize * lut3DSize];
        Color c101 = lut3DData[upperIndices.x + lowerIndices.y * lut3DSize + upperIndices.z * lut3DSize * lut3DSize];
        Color c110 = lut3DData[upperIndices.x + upperIndices.y * lut3DSize + lowerIndices.z * lut3DSize * lut3DSize];
        Color c111 = lut3DData[upperIndices.x + upperIndices.y * lut3DSize + upperIndices.z * lut3DSize * lut3DSize];

        // Interpolate between the eight LUT entries to find the remapped value
        Vector3 t = indices - lowerIndices;
        Color result =
            Color.Lerp(
                Color.Lerp(Color.Lerp(c000, c100, t.x), Color.Lerp(c010, c110, t.x), t.y),
                Color.Lerp(Color.Lerp(c001, c101, t.x), Color.Lerp(c011, c111, t.x), t.y),
                t.z);

        return result;
    }

    // Use the imported LUT data as needed
    public override void OnImportAsset(AssetImportContext ctx)
    {
        // Read the texture data from the file
        Import(ctx.assetPath);

        int size = overrideResolution > 0 ? overrideResolution : lut3DSize;

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

                    if (convertLinearToLogPre)
                    {
                        r = lct.LinearToLogC_Precise(r);
                        g = lct.LinearToLogC_Precise(g);
                        b = lct.LinearToLogC_Precise(b);
                    }

                    if (convertLogToLinearPre)
                    {
                        r = lct.LogCToLinear_Precise(r);
                        g = lct.LogCToLinear_Precise(g);
                        b = lct.LogCToLinear_Precise(b);
                    }

                    Color thisCol = new Color(r, g, b);

                    if (lut1DSize > 0) Map1D(thisCol);
                    if (lut3DSize > 0) Map3D(thisCol);
                    
                    
                    if (convertLinearToLogPost)
                    {
                        r = lct.LinearToLogC_Precise(r);
                        g = lct.LinearToLogC_Precise(g);
                        b = lct.LinearToLogC_Precise(b);
                    }

                    if (convertLogToLinearPost)
                    {
                        r = lct.LogCToLinear_Precise(r);
                        g = lct.LogCToLinear_Precise(g);
                        b = lct.LogCToLinear_Precise(b);
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
