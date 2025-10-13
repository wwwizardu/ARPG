#nullable enable
using ARPG.Tables;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D.Animation;

namespace ARPG.Creature
{
    public class CharacterInfo : MonoBehaviour
    {
        [SerializeField] protected SpriteLibraryAsset _spriteLibraryAsset;
        [SerializeField] protected Sprite _characterSprite;
        [SerializeField] protected SpriteRenderer _sr;
        [SerializeField] protected Animator _animator;
        [SerializeField] protected Image _hpBar;
        [SerializeField] protected Skill.SkillController _skillController;
        [SerializeField] protected TMPro.TextMeshPro _textName;

        public SpriteLibraryAsset SpriteLibraryAsset => _spriteLibraryAsset;
        public Sprite CharacterSprite => _characterSprite;
        public SpriteRenderer Sr => _sr;
        public Animator Animator => _animator;
        public Image HpBar => _hpBar;
        public Skill.SkillController SkillController => _skillController;
        public TMPro.TextMeshPro TextName => _textName;
    }
}