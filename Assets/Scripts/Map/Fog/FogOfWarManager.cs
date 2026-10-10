#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARPG.Map
{
    /// <summary>
    /// 시야·안개 상태 소유자 (FOG_OF_WAR_DESIGN). 플레이어 주변 64×64칸 창(벽에 가리지 않음·벽과 시설 모두에 가리지 않음)과
    /// 그 창을 담는 텍스처, 화면 덮개를 가진다. 저장하는 상태는 없다 — 시야는 로드·맵 전환 뒤 다시 계산한다.
    /// 시야 계산은 System_FogVision, 이동 엔티티 숨김은 System_FogEntityVisibility가 이 상태를 읽고 쓴다.
    /// </summary>
    public sealed class FogOfWarManager
    {
        public const float DEFAULT_VISION_RADIUS = 9f;

        private const float SHOW_MARGIN = 0.5f;           // 나타남은 반경 - 0.5칸, 사라짐은 반경 (경계 깜빡임 방지)
        // 화면 어둡기 (§3, 눈에 보이는 밝기 기준, 1 = 원래 밝기). 반경 - FADE_START_OFFSET칸부터 FAR_DISTANCE까지
        // 한 곡선으로 낮춘다. 두 smoothstep을 이어 붙이면 반경에서 기울기가 멈춰 회색 고리가 보인다.
        private const float VISION_FADE_START_OFFSET = 2f;
        private const float DISTANCE_FALLOFF_POWER = 2.65f; // 9칸에서 약 52%(FAR 30% 기준). 시야 반경에 중간 접합부를 만들지 않는다.
        private const float FAR_BRIGHTNESS = 0.3f;        // 시야 밖·깊은 그림자 밝기 (2026-10-10 5% → 30%, 검정 대신 지형이 보이게)
        private const float FAR_DISTANCE = 12f;
        // 시야 안 그림자 (확정 2026-10-10). 가려지기 시작한 곳은 벽 뒤 50%, 마을 시설(건물 점유 칸) 뒤 60%이고, 그림자 안쪽으로
        // SHADOW_DEPTH칸 들어갈수록 시야 밖 안개 밝기(FAR_BRIGHTNESS)까지 진해진다. 폭이 좁은 그림자는 깊어지지 않아 옅게 남는다.
        // 둘 다 그 뒤 엔티티는 숨긴다. (이전: 벽 35%·시설 50% 고정 — 시야 밖 안개와 따로 노는 회색 판으로 보였다)
        private const float WALL_SHADOW_EDGE_BRIGHTNESS = 0.5f;
        private const float FACILITY_SHADOW_EDGE_BRIGHTNESS = 0.6f;
        private const float SHADOW_DEPTH = 3f;
        private const int WINDOW_SIZE = 64;
        private const int WINDOW_HALF = WINDOW_SIZE / 2;
        private const int RECENTER_DISTANCE = 8;          // 창 중심에서 이만큼 벗어나면 창을 다시 잡는다
        // 월드(±808칸)·던전 좌표 어디든 덮는 크기. 맵 전환 직후 창을 다시 잡기 전에도 화면이 덮개 밖으로 나가지 않는다.
        // 화면 밖 부분은 래스터화되지 않으므로 그리기 비용은 화면 한 장과 같다.
        private const float OVERLAY_SIZE = 4096f;
        private const float OVERLAY_Z = -0.5f;
        // 정렬 레이어 Default의 가장 위. 월드 공간 캔버스(최대 100)보다 위, 화면 UI 카메라보다 아래.
        private const int OVERLAY_SORTING_ORDER = 32000;
        private const byte ON = 255;
        private static readonly Color FOG_COLOR = new(0.02f, 0.025f, 0.05f, 1f);

        private static readonly int _fogTexId = Shader.PropertyToID("_FogTex");
        private static readonly int _windowId = Shader.PropertyToID("_FogWindow");
        private static readonly int _visionId = Shader.PropertyToID("_FogVision");
        private static readonly int _shapeId = Shader.PropertyToID("_FogShape");
        private static readonly int _shadowId = Shader.PropertyToID("_FogShadow");
        private static readonly int _colorId = Shader.PropertyToID("_FogColor");

        // 창 버퍼. _visible = 지금 보임(벽과 시설 모두에 가리지 않음 — 엔티티 숨김·이펙트 판정 기준, 텍스처 B와 같다).
        // _pixels = RGBA32: R 벽에 가리지 않음, B 벽과 시설 모두에 가리지 않음(R과의 차이가 반투명 시설 그림자). G·A는 쓰지 않는다.
        private readonly byte[] _visible = new byte[WINDOW_SIZE * WINDOW_SIZE];
        private readonly byte[] _pixels = new byte[WINDOW_SIZE * WINDOW_SIZE * 4];
        private int _windowOriginX;
        private int _windowOriginY;
        private bool _hasWindow;
        private int _visionCellX;
        private int _visionCellY;
        private int _castRadius;
        private bool _hasVision;
        private bool _visionDirty = true;
        private Vector2 _visionCenter;
        private float _visionRadius = DEFAULT_VISION_RADIUS;

        private bool _initialized;
        private bool _enabled = true;

        private Texture2D? _texture;
        private Material? _material;
        private Mesh? _mesh;
        private GameObject? _overlay;
        private MeshRenderer? _renderer;

        /// <summary>안개가 동작 중인지. 꺼져 있으면 모든 조회가 "보임"을 돌려준다.</summary>
        public bool IsActive => _initialized == true && _enabled == true;
        /// <summary>현재 맵에서 시야를 한 번 이상 계산했는지. 맵 전환 직후에는 false.</summary>
        public bool HasVision => _hasVision;
        public bool IsEnabled => _enabled;

        public void Initialize()
        {
            EnsureResources();
            ResetWindow();
            _initialized = true;
        }

        public void Reset()
        {
            _initialized = false;
            _enabled = true;
            _visionRadius = DEFAULT_VISION_RADIUS;
            ResetWindow();
            if (_renderer != null) _renderer.enabled = false;
        }

        /// <summary>개발용 켜기/끄기. 끄면 덮개를 숨기고 숨김 대상이 모두 보이게 된다.</summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            _visionDirty = true;
            if (_renderer != null) _renderer.enabled = enabled == true && _hasVision == true;
        }

        /// <summary>막힌 칸이 바뀌었다(문 열고 닫기·벽 파괴·건물 배치). 다음 프레임에 시야를 다시 계산한다.</summary>
        public void MarkVisionDirty()
        {
            _visionDirty = true;
        }

        /// <summary>던전 입장·퇴장(로드 복원 포함)으로 맵이 바뀌었다. 창을 비우고 다음 프레임에 새 맵에서 시야를 다시 계산한다.</summary>
        public void OnMapChanged()
        {
            ResetWindow();
        }

        #region 조회

        /// <summary>칸이 지금 보이는지 (벽·시설 판정 포함, 반경은 계산 반경 기준). 시설 뒤 반투명 그림자 칸도 보이지 않는 칸이다.</summary>
        public bool IsVisibleCell(int x, int y)
        {
            if (IsActive == false) return true;
            if (_hasVision == false) return false;
            int localX = x - _windowOriginX;
            int localY = y - _windowOriginY;
            if ((uint)localX >= WINDOW_SIZE || (uint)localY >= WINDOW_SIZE) return false;
            return _visible[localY * WINDOW_SIZE + localX] != 0;
        }

        /// <summary>월드 좌표가 시야 원 안이면서 벽·시설에 가리지 않았는지. 이펙트 생성 판정에 쓴다.</summary>
        public bool IsVisibleWorld(Vector2 position)
        {
            if (IsActive == false) return true;
            if (_hasVision == false) return false;
            if ((position - _visionCenter).sqrMagnitude > _visionRadius * _visionRadius) return false;
            return IsVisibleCell(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
        }

        /// <summary>
        /// 이동 엔티티를 보여 줄지. 보이던 엔티티는 반경까지, 숨어 있던 엔티티는 반경 - 0.5칸 안에 들어와야 보인다.
        /// </summary>
        public bool IsEntityVisible(Vector2 position, bool wasVisible)
        {
            if (IsActive == false) return true;
            if (_hasVision == false) return false;
            float radius = wasVisible == true ? _visionRadius : _visionRadius - SHOW_MARGIN;
            if ((position - _visionCenter).sqrMagnitude > radius * radius) return false;
            return IsVisibleCell(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
        }

        /// <summary>원(공격 예고 범위)이 시야 원과 겹치는지. 벽 판정은 하지 않는다 — 맞을 수 있는 예고는 보여 준다.</summary>
        public bool OverlapsVision(Vector2 center, float radius)
        {
            if (IsActive == false) return true;
            if (_hasVision == false) return false;
            float reach = _visionRadius + radius;
            return (center - _visionCenter).sqrMagnitude <= reach * reach;
        }

        #endregion

        #region 시야 계산 (System_FogVision 전용)

        /// <summary>매 프레임 플레이어 실제 위치를 셰이더에 넘긴다. 원은 칸 단위로 끊기지 않는다.</summary>
        public void SetVisionCenter(Vector2 position, float radius)
        {
            _visionCenter = position;
            _visionRadius = radius;
            if (_material != null)
                _material.SetVector(_visionId, new Vector4(position.x, position.y, radius, VISION_FADE_START_OFFSET));
        }

        public bool NeedsVisionUpdate(int cellX, int cellY)
        {
            return _visionDirty == true || _hasVision == false || cellX != _visionCellX || cellY != _visionCellY;
        }

        /// <summary>
        /// 시야 계산을 시작한다. 창 중심에서 많이 벗어났으면 창을 다시 잡고, 아니면 지난 계산 범위만 지운다.
        /// </summary>
        public void BeginVisionUpdate(int cellX, int cellY, int castRadius)
        {
            int centerX = _windowOriginX + WINDOW_HALF;
            int centerY = _windowOriginY + WINDOW_HALF;
            if (_hasWindow == false ||
                Mathf.Abs(cellX - centerX) >= RECENTER_DISTANCE || Mathf.Abs(cellY - centerY) >= RECENTER_DISTANCE)
            {
                RecenterWindow(cellX, cellY);
            }
            else if (_hasVision == true)
            {
                ClearVisibleArea(_visionCellX, _visionCellY, _castRadius);
            }

            _visionCellX = cellX;
            _visionCellY = cellY;
            _castRadius = castRadius;
        }

        /// <summary>
        /// 칸을 벽에 가리지 않은 칸으로 표시한다(1차 계산). 계산 범위에 시설이 없으면 clearOfFacilities로
        /// "벽과 시설 모두에 가리지 않음(지금 보임)"을 함께 표시해 2차 계산을 건너뛴다.
        /// </summary>
        public void SetVisible(int x, int y, bool clearOfFacilities)
        {
            int localX = x - _windowOriginX;
            int localY = y - _windowOriginY;
            if ((uint)localX >= WINDOW_SIZE || (uint)localY >= WINDOW_SIZE) return;

            int index = localY * WINDOW_SIZE + localX;
            int pixel = index * 4;
            _pixels[pixel] = ON;
            if (clearOfFacilities == true)
            {
                _visible[index] = 1;
                _pixels[pixel + 2] = ON;
            }
        }

        /// <summary>
        /// 칸을 벽과 시설 모두에 가리지 않은 칸(지금 보임)으로 표시한다 (2차 계산). 엔티티 숨김·이펙트 판정과 화면의 시설 그림자가 함께 쓴다.
        /// 시설 뒤의 몬스터도 숨긴다 (확정 2026-10-10).
        /// </summary>
        public void SetClearOfFacilities(int x, int y)
        {
            int localX = x - _windowOriginX;
            int localY = y - _windowOriginY;
            if ((uint)localX >= WINDOW_SIZE || (uint)localY >= WINDOW_SIZE) return;
            int index = localY * WINDOW_SIZE + localX;
            _visible[index] = 1;
            _pixels[index * 4 + 2] = ON;
        }

        public void EndVisionUpdate()
        {
            _hasVision = true;
            _visionDirty = false;
            UploadTexture();
            if (_renderer != null) _renderer.enabled = _enabled;
        }

        #endregion

        private void RecenterWindow(int cellX, int cellY)
        {
            _windowOriginX = cellX - WINDOW_HALF;
            _windowOriginY = cellY - WINDOW_HALF;
            _hasWindow = true;
            System.Array.Clear(_visible, 0, _visible.Length);
            System.Array.Clear(_pixels, 0, _pixels.Length);

            if (_material != null)
                _material.SetVector(_windowId, new Vector4(_windowOriginX, _windowOriginY, 1f / WINDOW_SIZE, 1f / WINDOW_SIZE));
            if (_overlay != null)
                _overlay.transform.position = new Vector3(_windowOriginX + WINDOW_HALF, _windowOriginY + WINDOW_HALF, OVERLAY_Z);
        }

        private void ClearVisibleArea(int cellX, int cellY, int radius)
        {
            int minX = Mathf.Max(0, cellX - radius - _windowOriginX);
            int maxX = Mathf.Min(WINDOW_SIZE - 1, cellX + radius - _windowOriginX);
            int minY = Mathf.Max(0, cellY - radius - _windowOriginY);
            int maxY = Mathf.Min(WINDOW_SIZE - 1, cellY + radius - _windowOriginY);
            for (int localY = minY; localY <= maxY; localY++)
            {
                int row = localY * WINDOW_SIZE;
                for (int localX = minX; localX <= maxX; localX++)
                {
                    int pixel = (row + localX) * 4;
                    _visible[row + localX] = 0;
                    _pixels[pixel] = 0;
                    _pixels[pixel + 2] = 0;
                }
            }
        }

        /// <summary>창을 비운다. 맵 전환 직후 다음 시야 계산 전까지 화면 전체가 깊은 그림자(시야 밖 안개 밝기)로 덮여 이전 맵의 시야가 남지 않는다.</summary>
        private void ResetWindow()
        {
            _hasWindow = false;
            _hasVision = false;
            _visionDirty = true;
            System.Array.Clear(_visible, 0, _visible.Length);
            System.Array.Clear(_pixels, 0, _pixels.Length);
            UploadTexture();
        }

        private void UploadTexture()
        {
            if (_texture == null) return;
            _texture.SetPixelData(_pixels, 0);
            _texture.Apply(false, false);
        }

        private void EnsureResources()
        {
            if (_texture == null)
            {
                _texture = new Texture2D(WINDOW_SIZE, WINDOW_SIZE, TextureFormat.RGBA32, false, true)
                {
                    name = "FogOfWarWindow",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            if (_material == null)
            {
                Material? source = Resources.Load<Material>("FogOfWarMaterial");
                if (source == null)
                {
                    Debug.LogError("[FogOfWarManager] Resources/FogOfWarMaterial not found. Fog overlay is disabled.");
                    return;
                }
                // 런타임 값(텍스처·위치)이 에셋에 기록되지 않도록 복사본을 쓴다.
                _material = new Material(source) { name = "FogOfWarRuntimeMaterial" };
                _material.SetTexture(_fogTexId, _texture);
                _material.SetColor(_colorId, FOG_COLOR);
                _material.SetVector(_shapeId, new Vector4(DISTANCE_FALLOFF_POWER, FAR_BRIGHTNESS, FAR_DISTANCE, SHADOW_DEPTH));
                _material.SetVector(_shadowId, new Vector4(WALL_SHADOW_EDGE_BRIGHTNESS, FACILITY_SHADOW_EDGE_BRIGHTNESS, 0f, 0f));
            }

            if (_mesh == null)
            {
                float half = OVERLAY_SIZE * 0.5f;
                _mesh = new Mesh { name = "FogOfWarOverlay" };
                _mesh.SetVertices(new List<Vector3>
                {
                    new(-half, -half, 0f), new(half, -half, 0f), new(-half, half, 0f), new(half, half, 0f),
                });
                _mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
                _mesh.RecalculateBounds();
            }

            if (_overlay == null)
            {
                _overlay = new GameObject("FogOfWarOverlay");
                _overlay.transform.SetParent(AR.s.transform, false);
                _overlay.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _overlay.AddComponent<MeshRenderer>();
                _renderer.sharedMaterial = _material;
                _renderer.sortingLayerID = SortingLayer.NameToID("Default");
                _renderer.sortingOrder = OVERLAY_SORTING_ORDER;
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.lightProbeUsage = LightProbeUsage.Off;
                _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _renderer.enabled = false;
            }
        }
    }
}
