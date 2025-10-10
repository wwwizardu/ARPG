using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class ProceduralMeshGenerator : MonoBehaviour
{
    public enum ShapeType { Quad, Radial, Cylinder, Sphere, NineSliceQuad }

    public ShapeType shape = ShapeType.Quad;
    public Vector2 tiling = Vector2.one;
    [Range(-180f, 180f)] public float uvRotation = 0f;
    public bool autoUpdate = true;

    // Quad Settings
    public Vector2 quadSize = Vector2.one;

    // Quad (9-Slice) Settings
    public Vector2 border = new Vector2(0.1f, 0.1f);
    [Range(1, 10)] public int nineSliceSubdiv = 4;

    // Radial Settings
    public float innerRadius = 0.001f;
    public float outerRadius = 1f;
    public float angle = 360f;
    public int segments = 32;
    public int rings = 4;

    // Cylinder Settings
    public float cylinderHeight = 1f;
    public int cylinderSegments = 32;
    public int cylinderRings = 1;
    public float cylinderRadius = 1;
    public AnimationCurve cylinderProfile = AnimationCurve.Linear(0, 1, 1, 1);

    // Sphere Settings
    public int sphereSegments = 32; 
    public int sphereRings = 16;    
    public float sphereRadius = 1f;  

    private Mesh mesh;

    private void OnEnable()
    {
        GenerateMesh();
        SetupParticleSystemParameters();
    }

    private void OnValidate()
    {
        if (autoUpdate && Application.isEditor)
            GenerateMesh();
    }

    [ContextMenu("Generate Mesh")]
    public void GenerateMesh()
    {
        mesh = new Mesh();
        mesh.name = "P" + shape.ToString();

        switch (shape)
        {
            case ShapeType.Quad:
                GenerateQuad(mesh);
                break;
            case ShapeType.Radial:
                GenerateRadial(mesh);
                break;
            case ShapeType.Cylinder:
                GenerateCylinder(mesh);
                break;
            case ShapeType.Sphere:
                GenerateSphere(mesh);
                break;
            case ShapeType.NineSliceQuad:
                GenerateNineSliceQuad(mesh);
                break;

        }

        mesh.RecalculateNormals();
        UpdateParticleMesh();
    }

    private void GenerateQuad(Mesh mesh)
    {
        Vector2 halfSize = quadSize * 0.5f;

        Vector3[] vertices = new Vector3[4]
        {
        new Vector3(-halfSize.x, -halfSize.y, 0),
        new Vector3( halfSize.x, -halfSize.y, 0),
        new Vector3(-halfSize.x,  halfSize.y, 0),
        new Vector3( halfSize.x,  halfSize.y, 0)
        };

        int[] triangles = new int[6] { 0, 2, 1, 2, 3, 1 };

        Vector2[] uvs = new Vector2[4]
        {
        RotateUV(new Vector2(0, 0), uvRotation) * tiling,
        RotateUV(new Vector2(1, 0), uvRotation) * tiling,
        RotateUV(new Vector2(0, 1), uvRotation) * tiling,
        RotateUV(new Vector2(1, 1), uvRotation) * tiling
        };

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    private void GenerateNineSliceQuad(Mesh mesh)
    {
        float borderX = border.x;
        float borderY = border.y;

        float width = Mathf.Max(quadSize.x, borderX * 2f + 0.0001f);
        float height = Mathf.Max(quadSize.y, borderY * 2f + 0.0001f);

        float halfCenterW = (width - borderX * 2f) * 0.5f;
        float halfCenterH = (height - borderY * 2f) * 0.5f;

        float left = -halfCenterW - borderX;
        float innerLeft = -halfCenterW;
        float innerRight = halfCenterW;
        float right = halfCenterW + borderX;

        float bottom = -halfCenterH - borderY;
        float innerBottom = -halfCenterH;
        float innerTop = halfCenterH;
        float top = halfCenterH + borderY;

        Vector3[] vertices = new Vector3[16];
        Vector2[] uvs = new Vector2[16];

        float borderUV = 0.25f;

        float[] xPos = { left, innerLeft, innerRight, right };
        float[] yPos = { bottom, innerBottom, innerTop, top };

        float[] uVals = { 0f, borderUV, 1f - borderUV, 1f };
        float[] vVals = { 0f, borderUV, 1f - borderUV, 1f };

        int index = 0;
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                vertices[index] = new Vector3(xPos[x], yPos[y], 0f);
                uvs[index] = new Vector2(uVals[x], vVals[y]) * tiling;
                index++;
            }
        }

        int[] triangles = new int[9 * 6];
        int t = 0;
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                int i0 = y * 4 + x;
                int i1 = i0 + 1;
                int i2 = i0 + 4;
                int i3 = i2 + 1;

                triangles[t++] = i0;
                triangles[t++] = i2;
                triangles[t++] = i1;

                triangles[t++] = i1;
                triangles[t++] = i2;
                triangles[t++] = i3;
            }
        }
        for (int i = 0; i < uvs.Length; i++)
        {
            uvs[i] = RotateUV(uvs[i], uvRotation);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    private void GenerateRadial(Mesh mesh)
    {
        float angleRad = Mathf.Clamp(angle, 0.1f, 360f) * Mathf.Deg2Rad;
        float r0 = Mathf.Min(innerRadius, outerRadius);
        float r1 = Mathf.Max(innerRadius, outerRadius);

        int vertexCount = (rings + 1) * (segments + 1);
        int triangleCount = rings * segments * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        int vert = 0, tri = 0;

        for (int r = 0; r <= rings; r++)
        {
            float tR = (float)r / rings;
            float radius = Mathf.Lerp(r0, r1, tR);

            for (int s = 0; s <= segments; s++)
            {
                float tS = (float)s / segments;
                float a = tS * angleRad;

                float x = Mathf.Cos(a) * radius;
                float y = Mathf.Sin(a) * radius;

                vertices[vert] = new Vector3(x, y, 0);

                float u = tS;
                float v = (radius - r0) / (r1 - r0);
                uvs[vert] = new Vector2(u, v) * tiling;

                if (r < rings && s < segments)
                {
                    int current = vert;
                    int next = vert + segments + 1;

                    triangles[tri++] = current;
                    triangles[tri++] = current + 1;
                    triangles[tri++] = next;

                    triangles[tri++] = current + 1;
                    triangles[tri++] = next + 1;
                    triangles[tri++] = next;
                }

                vert++;
            }
        }
        for (int i = 0; i < uvs.Length; i++)
        {
            uvs[i] = RotateUV(uvs[i], uvRotation);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    private void GenerateCylinder(Mesh mesh)
    {
        int seg = Mathf.Max(3, cylinderSegments);
        int rings = Mathf.Max(1, cylinderRings);

        int vertexCount = (rings + 1) * (seg + 1);
        int triangleCount = rings * seg * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        int vert = 0, tri = 0;

        for (int r = 0; r <= rings; r++)
        {
            float tR = (float)r / rings;

            float radius = cylinderProfile.Evaluate(tR) * cylinderRadius;

            float y = Mathf.Lerp(-cylinderHeight * 0.5f, cylinderHeight * 0.5f, tR);

            for (int s = 0; s <= seg; s++)
            {
                float tS = (float)s / seg;
                float angle = tS * Mathf.PI * 2f;

                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                vertices[vert] = new Vector3(x, z, y);
                uvs[vert] = new Vector2(tS, tR) * tiling;

                if (r < rings && s < seg)
                {
                    int current = vert;
                    int next = vert + seg + 1;

                    triangles[tri++] = current;
                    triangles[tri++] = current + 1;
                    triangles[tri++] = next;

                    triangles[tri++] = current + 1;
                    triangles[tri++] = next + 1;
                    triangles[tri++] = next;
                }

                vert++;
            }
        }
        for (int i = 0; i < uvs.Length; i++)
        {
            uvs[i] = RotateUV(uvs[i], uvRotation);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }

    private void GenerateSphere(Mesh mesh)
    {
        int seg = Mathf.Max(3, sphereSegments);
        int rings = Mathf.Max(2, sphereRings);

        int vertexCount = (rings + 1) * (seg + 1);
        int triangleCount = rings * seg * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        int vert = 0;
        int tri = 0;

        for (int r = 0; r <= rings; r++)
        {
            float v = (float)r / rings;
            float lat = Mathf.PI * v - Mathf.PI / 2f;

            float y = Mathf.Sin(lat);
            float radius = Mathf.Cos(lat);

            for (int s = 0; s <= seg; s++)
            {
                float u = (float)s / seg;
                float lon = u * Mathf.PI * 2f;

                float x = Mathf.Cos(lon) * radius;
                float z = Mathf.Sin(lon) * radius;

                vertices[vert] = new Vector3(x, z, y) * sphereRadius;
                uvs[vert] = new Vector2(u, v) * tiling;

                if (r < rings && s < seg)
                {
                    int current = vert;
                    int next = vert + seg + 1;

                    triangles[tri++] = current;
                    triangles[tri++] = current + 1;
                    triangles[tri++] = next;

                    triangles[tri++] = current + 1;
                    triangles[tri++] = next + 1;
                    triangles[tri++] = next;
                }

                vert++;
            }
        }
        for (int i = 0; i < uvs.Length; i++)
        {
            uvs[i] = RotateUV(uvs[i], uvRotation);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
    }


    private void SetupParticleSystemParameters()
    {
        var ps = GetComponent<ParticleSystem>();
        if (ps == null)
            ps = gameObject.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = false;
        main.startLifetime = 1f;
        main.startSpeed = 0f;
        main.startSize = 1f;

        main.startRotation3D = true;
        main.startRotationX = Mathf.Deg2Rad * 90f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var shape = ps.shape;
        shape.enabled = false;

        var psr = GetComponent<ParticleSystemRenderer>();
        if (psr == null)
            psr = gameObject.AddComponent<ParticleSystemRenderer>();

        psr.renderMode = ParticleSystemRenderMode.Mesh;
        psr.alignment = ParticleSystemRenderSpace.Local;

        UpdateParticleMesh();
    }

    private void UpdateParticleMesh()
    {
        var psr = GetComponent<ParticleSystemRenderer>();
        if (psr != null && mesh != null)
            psr.mesh = mesh;
    }

    private Vector2 RotateUV(Vector2 uv, float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        uv -= new Vector2(0.5f, 0.5f);
        float x = uv.x * cos - uv.y * sin;
        float y = uv.x * sin + uv.y * cos;
        return new Vector2(x, y) + new Vector2(0.5f, 0.5f);
    }

#if UNITY_EDITOR
    [ContextMenu("Reassign Particle Mesh")]
    public void ReassignParticleMesh()
    {
        var psr = GetComponent<ParticleSystemRenderer>();
        if (psr != null && mesh != null)
        {
            psr.mesh = mesh;
            Debug.Log($"Mesh reassigned to ParticleSystemRenderer on '{gameObject.name}'.");
        }
        else
        {
            Debug.LogWarning($"Could not reassign mesh on '{gameObject.name}'. Mesh or PSR missing.");
        }
    }
#endif

}
