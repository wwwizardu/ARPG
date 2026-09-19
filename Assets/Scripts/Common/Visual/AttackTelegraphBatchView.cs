#nullable enable
using System.Collections.Generic;
using ARPG.Skill.Telegraph;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARPG.View
{
    public enum AttackTelegraphRenderDetail : byte
    {
        Full,
        SurfaceAndOutline,
        OutlineOnly,
    }

    public readonly struct AttackTelegraphRenderData
    {
        public readonly AttackTelegraphShape Shape;
        public readonly Vector2 Origin;
        public readonly Vector2 Direction;
        public readonly float Range;
        public readonly float HalfAngleDeg;
        public readonly float Width;
        public readonly int ProjectileCount;
        public readonly float ProjectileSpreadAngleDeg;
        public readonly float WarningStartFixedTime;
        public readonly float NextCommitFixedTime;
        public readonly AttackTelegraphRenderDetail Detail;

        public AttackTelegraphRenderData(
            AttackTelegraphShape shape,
            Vector2 origin,
            Vector2 direction,
            float range,
            float halfAngleDeg,
            float width,
            int projectileCount,
            float projectileSpreadAngleDeg,
            float warningStartFixedTime,
            float nextCommitFixedTime,
            AttackTelegraphRenderDetail detail)
        {
            Shape = shape;
            Origin = origin;
            Direction = direction;
            Range = range;
            HalfAngleDeg = halfAngleDeg;
            Width = width;
            ProjectileCount = projectileCount;
            ProjectileSpreadAngleDeg = projectileSpreadAngleDeg;
            WarningStartFixedTime = warningStartFixedTime;
            NextCommitFixedTime = nextCommitFixedTime;
            Detail = detail;
        }
    }

    /// <summary>
    /// 모든 일반 공격 예고를 하나의 동적 Mesh와 Material로 메인 카메라에 직접 제출합니다.
    /// 공격별 GameObject, Renderer, Material 또는 RenderTexture를 만들지 않습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackTelegraphBatchView : MonoBehaviour
    {
        private const int CIRCLE_SEGMENTS = 48;
        private const int LANE_CAP_SEGMENTS = 8;
        private const float MIN_LANE_RADIUS = 0.01f;
        private const float MIN_PATH_LENGTH = 0.001f;
        private const float GROUND_OVERLAY_Z = -0.001f;

        private static readonly int _currentTimeId = Shader.PropertyToID("_CurrentTime");

        private readonly List<Vector3> _positions = new(6144);
        private readonly List<Vector2> _uv0 = new(6144);
        private readonly List<Vector2> _uv1 = new(6144);
        private readonly List<Vector2> _uv2 = new(6144);
        private readonly List<Vector2> _uv3 = new(6144);
        private readonly List<Color32> _colors = new(6144);
        private readonly List<int> _indices = new(18432);

        private Mesh? _mesh;
        private Material? _material;
        private MaterialPropertyBlock? _properties;
        private bool _ownsMaterial;

        public void BeginFrame()
        {
            EnsureResources();
            _positions.Clear();
            _uv0.Clear();
            _uv1.Clear();
            _uv2.Clear();
            _uv3.Clear();
            _colors.Clear();
            _indices.Clear();
        }

        public void Add(in AttackTelegraphRenderData data)
        {
            switch (data.Shape)
            {
                case AttackTelegraphShape.OwnerCircle:
                case AttackTelegraphShape.TargetCircle:
                case AttackTelegraphShape.LandingCircle:
                    AddRadial(data, 180f);
                    break;

                case AttackTelegraphShape.OwnerSector:
                    AddRadial(data, data.HalfAngleDeg);
                    break;

                case AttackTelegraphShape.ProjectileLane:
                case AttackTelegraphShape.ProjectileFan:
                    AddProjectilePaths(data);
                    break;
            }
        }

        public void Render(Camera? camera, float currentTime)
        {
            if (camera == null || _mesh == null || _material == null || _properties == null)
                return;
            if (_indices.Count == 0)
                return;

            _mesh.Clear(true);
            _mesh.SetVertices(_positions);
            _mesh.SetUVs(0, _uv0);
            _mesh.SetUVs(1, _uv1);
            _mesh.SetUVs(2, _uv2);
            _mesh.SetUVs(3, _uv3);
            _mesh.SetColors(_colors);
            _mesh.SetIndices(_indices, MeshTopology.Triangles, 0, true);

            _properties.Clear();
            _properties.SetFloat(_currentTimeId, currentTime);
            Graphics.DrawMesh(
                _mesh,
                Matrix4x4.identity,
                _material,
                0,
                camera,
                0,
                _properties,
                ShadowCastingMode.Off,
                false,
                null,
                false);
        }

        public void ClearBatch()
        {
            _positions.Clear();
            _uv0.Clear();
            _uv1.Clear();
            _uv2.Clear();
            _uv3.Clear();
            _colors.Clear();
            _indices.Clear();
            if (_mesh != null)
            {
                _mesh.Clear(true);
            }
        }

        private void Awake()
        {
            EnsureResources();
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }

            if (_ownsMaterial == true && _material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        private void EnsureResources()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    name = "AttackTelegraphCombinedMesh",
                    indexFormat = IndexFormat.UInt32,
                };
                _mesh.MarkDynamic();
            }

            _properties ??= new MaterialPropertyBlock();

            if (_material == null)
            {
                _material = Resources.Load<Material>("AttackTelegraphMaterial");
                if (_material == null)
                {
                    Shader shader = Shader.Find("ARPG/Attack Telegraph Unlit");
                    if (shader != null)
                    {
                        _material = new Material(shader)
                        {
                            name = "AttackTelegraphRuntimeMaterial",
                            renderQueue = 3000,
                        };
                        _ownsMaterial = true;
                    }
                    else
                    {
                        Debug.LogError("[AttackTelegraphBatchView] Attack telegraph shader/material not found.");
                    }
                }
            }
        }

        private void AddRadial(in AttackTelegraphRenderData data, float requestedHalfAngleDeg)
        {
            float range = Mathf.Max(0.01f, data.Range);
            float halfAngle = Mathf.Clamp(requestedHalfAngleDeg, 0f, 180f);
            float totalAngle = halfAngle * 2f;
            int segments = halfAngle >= 180f
                ? CIRCLE_SEGMENTS
                : Mathf.Clamp(Mathf.CeilToInt(totalAngle / 7.5f), 2, CIRCLE_SEGMENTS);
            Vector2 forward = AttackTelegraphGeometryResolver.NormalizeOrFallback(data.Direction, Vector2.right);
            float baseAngle = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg;

            int vertexStart = _positions.Count;
            AddVertex(
                data.Origin,
                Vector2.zero,
                data.WarningStartFixedTime,
                data.NextCommitFixedTime,
                0f,
                halfAngle * Mathf.Deg2Rad,
                data.Detail);

            for (int i = 0; i <= segments; i++)
            {
                float ratio = (float)i / segments;
                float relativeAngle = -halfAngle + totalAngle * ratio;
                float radians = (baseAngle + relativeAngle) * Mathf.Deg2Rad;
                Vector2 local = new Vector2(
                    Mathf.Cos(relativeAngle * Mathf.Deg2Rad),
                    Mathf.Sin(relativeAngle * Mathf.Deg2Rad));
                Vector2 world = data.Origin + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * range;
                AddVertex(
                    world,
                    local,
                    data.WarningStartFixedTime,
                    data.NextCommitFixedTime,
                    0f,
                    halfAngle * Mathf.Deg2Rad,
                    data.Detail);
            }

            for (int i = 0; i < segments; i++)
            {
                _indices.Add(vertexStart);
                _indices.Add(vertexStart + i + 1);
                _indices.Add(vertexStart + i + 2);
            }
        }

        private void AddProjectilePaths(in AttackTelegraphRenderData data)
        {
            int count = Mathf.Max(1, data.ProjectileCount);
            float totalSpread = (count - 1) * data.ProjectileSpreadAngleDeg;
            float startOffset = -totalSpread * 0.5f;
            Vector2 baseDirection = AttackTelegraphGeometryResolver.NormalizeOrFallback(data.Direction, Vector2.right);
            float baseAngle = Mathf.Atan2(baseDirection.y, baseDirection.x) * Mathf.Rad2Deg;

            for (int i = 0; i < count; i++)
            {
                float angle = baseAngle + startOffset + data.ProjectileSpreadAngleDeg * i;
                float radians = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                AddLane(data, direction);
            }
        }

        private void AddLane(in AttackTelegraphRenderData data, Vector2 direction)
        {
            float length = Mathf.Max(MIN_PATH_LENGTH, data.Range);
            float radius = Mathf.Max(MIN_LANE_RADIUS, data.Width * 0.5f);
            Vector2 forward = AttackTelegraphGeometryResolver.NormalizeOrFallback(direction, Vector2.right);
            Vector2 left = new Vector2(-forward.y, forward.x);
            int vertexStart = _positions.Count;
            int boundaryCount = (LANE_CAP_SEGMENTS + 1) * 2;
            float aspect = length / radius;

            Vector2 center = data.Origin + forward * (length * 0.5f);
            AddVertex(
                center,
                new Vector2(0.5f, 0f),
                data.WarningStartFixedTime,
                data.NextCommitFixedTime,
                1f,
                aspect,
                data.Detail);

            for (int i = 0; i <= LANE_CAP_SEGMENTS; i++)
            {
                float angle = Mathf.Lerp(90f, 270f, (float)i / LANE_CAP_SEGMENTS) * Mathf.Deg2Rad;
                float localX = Mathf.Cos(angle) * radius;
                float localY = Mathf.Sin(angle) * radius;
                Vector2 world = data.Origin + forward * localX + left * localY;
                AddVertex(
                    world,
                    new Vector2(localX / length, localY / radius),
                    data.WarningStartFixedTime,
                    data.NextCommitFixedTime,
                    1f,
                    aspect,
                    data.Detail);
            }

            Vector2 end = data.Origin + forward * length;
            for (int i = 0; i <= LANE_CAP_SEGMENTS; i++)
            {
                float angle = Mathf.Lerp(-90f, 90f, (float)i / LANE_CAP_SEGMENTS) * Mathf.Deg2Rad;
                float localX = Mathf.Cos(angle) * radius;
                float localY = Mathf.Sin(angle) * radius;
                Vector2 world = end + forward * localX + left * localY;
                AddVertex(
                    world,
                    new Vector2(1f + localX / length, localY / radius),
                    data.WarningStartFixedTime,
                    data.NextCommitFixedTime,
                    1f,
                    aspect,
                    data.Detail);
            }

            for (int i = 0; i < boundaryCount; i++)
            {
                int current = vertexStart + i + 1;
                int next = vertexStart + ((i + 1) % boundaryCount) + 1;
                _indices.Add(vertexStart);
                _indices.Add(current);
                _indices.Add(next);
            }
        }

        private void AddVertex(
            Vector2 worldPosition,
            Vector2 normalizedPosition,
            float warningStart,
            float nextCommit,
            float shaderShape,
            float shaderParameter,
            AttackTelegraphRenderDetail detail)
        {
            // Default sorting order 0의 지형 뒤가 아니라 바로 위에 놓고,
            // sorting order 1의 엔티티/오브젝트는 계속 예고보다 앞에 둡니다.
            _positions.Add(new Vector3(worldPosition.x, worldPosition.y, GROUND_OVERLAY_Z));
            _uv0.Add(normalizedPosition);
            _uv1.Add(new Vector2(warningStart, nextCommit));
            _uv2.Add(new Vector2(shaderShape, shaderParameter));
            _uv3.Add(new Vector2((float)detail, 0f));
            _colors.Add(new Color32(255, 70, 24, 255));
        }
    }
}
