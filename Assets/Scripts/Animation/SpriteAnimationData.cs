using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

namespace ARPG
{
    /// <summary>
    /// SpriteLibraryAsset에서 스프라이트를 미리 캐싱하는 런타임 데이터 클래스.
    /// System_Animation의 Dictionary에 저장되어 PlayableAnimator를 대체.
    /// AnimCategory enum 인덱스로 배열 접근하여 GC 없이 빠른 조회.
    /// </summary>
    public class SpriteAnimationData
    {
        private static readonly int ANIM_CATEGORY_COUNT = Enum.GetValues(typeof(GlobalEnum.AnimCategory)).Length;

        private SpriteRenderer _spriteRenderer;
        private Sprite[][] _categoryFrames; // [AnimCategory][frameIndex]

        public SpriteRenderer Renderer => _spriteRenderer;

        public SpriteAnimationData(SpriteRenderer spriteRenderer, SpriteLibraryAsset slAsset)
        {
            _spriteRenderer = spriteRenderer;
            _categoryFrames = new Sprite[ANIM_CATEGORY_COUNT][];
            CacheSprites(slAsset);
        }

        private void CacheSprites(SpriteLibraryAsset slAsset)
        {
            // 모든 AnimCategory에 대해 스프라이트 캐싱
            var categories = (GlobalEnum.AnimCategory[])Enum.GetValues(typeof(GlobalEnum.AnimCategory));
            for (int c = 0; c < categories.Length; c++)
            {
                GlobalEnum.AnimCategory category = categories[c];
                string categoryName = category.ToString();

                List<Sprite> frames = new List<Sprite>();
                int index = 1;

                while (true)
                {
                    string label = categoryName + index.ToString();
                    Sprite sprite = slAsset.GetSprite(categoryName, label);

                    if (sprite == null)
                        break;

                    frames.Add(sprite);
                    index++;
                }

                _categoryFrames[(int)category] = frames.Count > 0 ? frames.ToArray() : Array.Empty<Sprite>();
            }
        }

        /// <summary>
        /// 해당 카테고리가 존재하는지 (프레임 수 > 0)
        /// </summary>
        public bool HasCategory(GlobalEnum.AnimCategory category)
        {
            int idx = (int)category;
            if (idx < 0 || idx >= _categoryFrames.Length)
                return false;

            return _categoryFrames[idx].Length > 0;
        }

        /// <summary>
        /// 해당 카테고리의 프레임 수 반환
        /// </summary>
        public int GetFrameCount(GlobalEnum.AnimCategory category)
        {
            int idx = (int)category;
            if (idx < 0 || idx >= _categoryFrames.Length)
                return 0;

            return _categoryFrames[idx].Length;
        }

        /// <summary>
        /// 스프라이트 반환. 카테고리가 없으면 Idle로 폴백.
        /// </summary>
        public Sprite GetSprite(GlobalEnum.AnimCategory category, int frameIndex)
        {
            int idx = (int)category;
            Sprite[] frames = null;

            // 해당 카테고리 프레임 확인
            if (idx >= 0 && idx < _categoryFrames.Length && _categoryFrames[idx].Length > 0)
            {
                frames = _categoryFrames[idx];
            }
            else
            {
                // Idle로 폴백
                frames = _categoryFrames[(int)GlobalEnum.AnimCategory.Idle];
                if (frames == null || frames.Length == 0)
                    return null;
            }

            // 인덱스 클램핑
            if (frameIndex < 0)
                frameIndex = 0;
            if (frameIndex >= frames.Length)
                frameIndex = frames.Length - 1;

            return frames[frameIndex];
        }

        /// <summary>
        /// SpriteRenderer에 직접 스프라이트 설정
        /// </summary>
        public void SetSprite(GlobalEnum.AnimCategory category, int frameIndex)
        {
            if (_spriteRenderer == null)
                return;

            Sprite sprite = GetSprite(category, frameIndex);
            if (sprite != null)
            {
                _spriteRenderer.sprite = sprite;
            }
        }
    }
}
