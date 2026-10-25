#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine.InputSystem;

namespace ARPG.Input
{
    /// <summary>Device-independent gameplay actions and their immutable default bindings.</summary>
    public static class GameInputBindings
    {
        public const string KeyboardGroup = "Keyboard&Mouse";
        public const string GamepadGroup = "Gamepad";
        public readonly struct Definition
        {
            public readonly string Name, Label, Primary, Secondary, Pad;
            public readonly bool Modified;
            public Definition(string name, string label, string primary, string secondary, string pad, bool modified = false)
            { Name = name; Label = label; Primary = primary; Secondary = secondary; Pad = pad; Modified = modified; }
        }

        public static readonly string[] SkillNames = { "Attack", "Jump", "Skill2", "Skill3", "Skill4", "Skill5", "Skill6", "Skill7", "Skill8", "Skill9" };
        public static readonly string[] ItemNames = { "QuickItem0", "QuickItem1", "QuickItem2", "QuickItem3" };
        public static readonly Definition[] Definitions =
        {
            new("Up", "위로 이동", "<Keyboard>/w", "<Keyboard>/upArrow", ""),
            new("Down", "아래로 이동", "<Keyboard>/s", "<Keyboard>/downArrow", ""),
            new("Left", "왼쪽 이동", "<Keyboard>/a", "<Keyboard>/leftArrow", ""),
            new("Right", "오른쪽 이동", "<Keyboard>/d", "<Keyboard>/rightArrow", ""),
            new("Interact", "상호작용 / 줍기", "<Keyboard>/e", "", "<Gamepad>/leftStickPress"),
            new("Sprint", "달리기", "<Keyboard>/leftShift", "", "<Gamepad>/rightShoulder"),
            new("Inventory", "인벤토리", "<Keyboard>/i", "", "<Gamepad>/select"),
            new("SkillBook", "스킬북", "<Keyboard>/k", "", "<Gamepad>/select", true),
            new("CharmBoard", "참 판", "<Keyboard>/p", "", "<Gamepad>/rightStickPress"),
            new("Village", "마을 정보", "<Keyboard>/v", "", ""),
            new("QuickSlots", "아이템 퀵슬롯 등록", "<Keyboard>/o", "", "<Gamepad>/rightStickPress", true),
            new("Attack", "스킬 슬롯 1", "<Mouse>/leftButton", "<Keyboard>/enter", "<Gamepad>/rightTrigger"),
            new("Jump", "스킬 슬롯 2", "<Keyboard>/space", "", "<Gamepad>/leftTrigger"),
            new("Skill2", "스킬 슬롯 3", "<Keyboard>/1", "", "<Gamepad>/buttonWest"),
            new("Skill3", "스킬 슬롯 4", "<Keyboard>/2", "", "<Gamepad>/buttonNorth"),
            new("Skill4", "스킬 슬롯 5", "<Keyboard>/3", "", "<Gamepad>/buttonSouth"),
            new("Skill5", "스킬 슬롯 6", "<Keyboard>/4", "", "<Gamepad>/buttonEast"),
            new("Skill6", "스킬 슬롯 7", "<Keyboard>/5", "", "<Gamepad>/buttonWest", true),
            new("Skill7", "스킬 슬롯 8", "<Keyboard>/6", "", "<Gamepad>/buttonNorth", true),
            new("Skill8", "스킬 슬롯 9", "<Keyboard>/7", "", "<Gamepad>/buttonSouth", true),
            new("Skill9", "스킬 슬롯 10", "<Keyboard>/8", "", "<Gamepad>/buttonEast", true),
            new("QuickItem0", "아이템 슬롯 1", "<Keyboard>/f1", "", "<Gamepad>/dpad/up"),
            new("QuickItem1", "아이템 슬롯 2", "<Keyboard>/f2", "", "<Gamepad>/dpad/right"),
            new("QuickItem2", "아이템 슬롯 3", "<Keyboard>/f3", "", "<Gamepad>/dpad/down"),
            new("QuickItem3", "아이템 슬롯 4", "<Keyboard>/f4", "", "<Gamepad>/dpad/left"),
        };

        public static void Configure(InputActionAsset asset)
        {
            asset.Disable();
            InputActionMap map = asset.FindActionMap("Player", true);
            InputAction movement = map.FindAction("Move", true);
            while (movement.bindings.Count > 0) movement.ChangeBinding(0).Erase();
            Add(movement, "<Gamepad>/leftStick", GamepadGroup, "Move", 0);
            InputAction pointer = map.FindAction("MouseMove", true);
            while (pointer.bindings.Count > 0) pointer.ChangeBinding(0).Erase();
            Add(pointer, "<Mouse>/position", KeyboardGroup, "Pointer", 0);
            for (int i = 0; i < Definitions.Length; i++)
            {
                Definition definition = Definitions[i];
                InputAction action = map.FindAction(definition.Name) ?? map.AddAction(definition.Name, InputActionType.Button);
                while (action.bindings.Count > 0) action.ChangeBinding(0).Erase();
                Add(action, definition.Primary, KeyboardGroup, "Primary", 0);
                Add(action, definition.Secondary, KeyboardGroup, "Secondary", 1);
                if (definition.Pad.Length == 0) continue;
                if (definition.Modified == true)
                {
                    Add(action, "ButtonWithOneModifier", GamepadGroup, "LB + button", 2, composite: true);
                    Add(action, "<Gamepad>/leftShoulder", GamepadGroup, "Modifier", 3, part: true);
                    Add(action, definition.Pad, GamepadGroup, "Button", 4, part: true);
                }
                else Add(action, definition.Pad, GamepadGroup, "Gamepad", 2);
            }
            InputAction menu = map.FindAction("Menu", true);
            if (menu.bindings.Count < 2) menu.AddBinding("<Gamepad>/start", groups: GamepadGroup);
            InputAction cancel = asset.FindAction("UI/Cancel", true);
            while (cancel.bindings.Count > 0) cancel.ChangeBinding(0).Erase();
            Add(cancel, "<Keyboard>/escape", KeyboardGroup, "Cancel", 0);
            Add(cancel, "<Gamepad>/buttonEast", GamepadGroup, "Back", 1);
            Add(cancel, "<Gamepad>/start", GamepadGroup, "Menu", 2);
            InputAction comparison = asset.FindAction("UI/CompareEquipment", true);
            bool hasPadComparison = false;
            for (int i = 0; i < comparison.bindings.Count; i++)
                if (comparison.bindings[i].path == "<Gamepad>/leftTrigger") hasPadComparison = true;
            if (hasPadComparison == false) Add(comparison, "<Gamepad>/leftTrigger", GamepadGroup, "Compare", 2);
            InputAction closeInventory = asset.FindAction("UI/CloseInventory", true);
            if (closeInventory.bindings.Count < 2) Add(closeInventory, "", KeyboardGroup, "Secondary", 1);
        }

        public static bool IsModified(string name)
        {
            for (int i = 0; i < Definitions.Length; i++)
                if (Definitions[i].Name == name) return Definitions[i].Modified;
            return false;
        }

        public static int BindingIndex(InputAction action, bool gamepad, int keyboardSlot = 0)
        {
            string group = gamepad == true ? GamepadGroup : KeyboardGroup;
            int found = 0;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite == true || (binding.isPartOfComposite == true && binding.name != "Button")) continue;
                if (binding.groups != group) continue;
                if (gamepad == true || found++ == keyboardSlot) return i;
            }
            return -1;
        }

        private static void Add(InputAction action, string path, string group, string name, int index, bool composite = false, bool part = false)
        {
            using MD5 hash = MD5.Create();
            Guid id = new(hash.ComputeHash(Encoding.UTF8.GetBytes("ARPG/" + action.actionMap.name + "/" + action.name + "/" + index)));
            action.AddBinding(new InputBinding { id = id, path = path, groups = group, name = name, isComposite = composite, isPartOfComposite = part });
        }
    }
}
